// KuTax on your own domain: this Worker receives kutax.com.au (and www) and forwards to the Cloud Run service.
// Cloud Run routes by host name, so the request goes to its run.app address; a shared secret proves to the app that
// it came through here (the app refuses anything else), and the visitor's real address is passed along for the app's
// sign-in rate limits.

/**
 * @typedef {object} Env
 * @property {string} ORIGIN_URL The Cloud Run address, e.g. https://kutax-123.australia-southeast1.run.app
 * @property {string} CANONICAL_HOST The one host name KuTax answers on, e.g. kutax.com.au (others redirect to it)
 * @property {string} ORIGIN_SHARED_SECRET Set with `wrangler secret put`; the same value as Secret Manager's kutax-origin-secret
 */

const VERIFY_HEADER = 'X-KuTax-Origin-Verify';
const CLIENT_HEADER = 'X-KuTax-Client-IP';
// Angular's build output with content hashes in the name: safe to cache for a year.
const HASHED_ASSET = /^\/(?:(?:chunk|main|polyfills|styles)-[A-Za-z0-9_-]+\.(?:js|css)|media\/[^/]+)$/;

/** @param {string | undefined} value */
export function readOrigin(value) {
  if (!value) throw new Error('ORIGIN_URL is not configured.');
  const origin = new URL(value);
  if (origin.protocol !== 'https:') throw new Error('ORIGIN_URL must use HTTPS.');
  if (origin.pathname !== '/' || origin.search || origin.hash) throw new Error('ORIGIN_URL must be an origin only.');
  return origin;
}

/** @param {string | undefined} value */
export function readSecret(value) {
  if (typeof value !== 'string' || new TextEncoder().encode(value).length < 32) throw new Error('ORIGIN_SHARED_SECRET is missing or too short.');
  return value;
}

/** Hashed build files are cached at Cloudflare's edge; everything else (pages, API) goes to KuTax every time. */
export function cachePolicyFor(/** @type {Request} */ request, /** @type {string} */ pathname) {
  if (request.method !== 'GET' || request.headers.has('Range') || !HASHED_ASSET.test(pathname)) return undefined;
  return { cacheEverything: true, cacheTtlByStatus: { '200-299': 31536000, '404': 60, '500-599': 0 } };
}

/** A redirect KuTax sends to its run.app address is pointed back at the public host. */
export function rewriteRedirect(/** @type {Response} */ response, /** @type {URL} */ origin, /** @type {URL} */ incoming) {
  const location = response.headers.get('Location');
  if (!location || response.status < 300 || response.status > 399) return response;
  const target = new URL(location, origin);
  if (target.origin !== origin.origin) return response;
  target.protocol = incoming.protocol;
  target.host = incoming.host;
  const headers = new Headers(response.headers);
  headers.set('Location', target.toString());
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}

/**
 * @param {Request} request
 * @param {Env} env
 * @param {typeof fetch} fetchImpl
 */
export async function proxyRequest(request, env, fetchImpl = fetch) {
  let origin, secret;
  try {
    origin = readOrigin(env?.ORIGIN_URL);
    secret = readSecret(env?.ORIGIN_SHARED_SECRET);
  } catch (error) {
    console.error(JSON.stringify({ message: 'proxy not configured', error: error instanceof Error ? error.message : String(error) }));
    return new Response('KuTax is not configured.', { status: 500 });
  }
  const incoming = new URL(request.url);
  // One address: www (or any other name routed here) goes to the canonical host.
  if (env.CANONICAL_HOST && incoming.hostname !== env.CANONICAL_HOST) {
    const canonical = new URL(incoming);
    canonical.hostname = env.CANONICAL_HOST;
    return Response.redirect(canonical.toString(), 301);
  }
  const upstream = new Request(new URL(incoming.pathname + incoming.search, origin), new Request(request, { redirect: 'manual' }));
  upstream.headers.set('X-Forwarded-Host', incoming.host);
  upstream.headers.set('X-Forwarded-Proto', 'https');
  // Whatever a client sent in these is replaced: only this Worker sets them.
  upstream.headers.set(VERIFY_HEADER, secret);
  upstream.headers.delete(CLIENT_HEADER);
  const client = request.headers.get('CF-Connecting-IP');
  if (client) upstream.headers.set(CLIENT_HEADER, client);
  const cache = cachePolicyFor(request, incoming.pathname);
  try {
    const response = await fetchImpl(upstream, cache ? { cf: cache } : undefined);
    return rewriteRedirect(response, origin, incoming);
  } catch (error) {
    console.error(JSON.stringify({ message: 'origin unreachable', error: error instanceof Error ? error.message : String(error) }));
    return new Response('KuTax is starting or unreachable. Try again in a moment.', { status: 502, headers: { 'Retry-After': '5' } });
  }
}

export default {
  /** @param {Request} request @param {Env} env */
  fetch(request, env) {
    return proxyRequest(request, env);
  },
};
