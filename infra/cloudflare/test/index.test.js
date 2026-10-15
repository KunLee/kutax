import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cachePolicyFor, proxyRequest } from '../src/index.js';

const env = {
  ORIGIN_URL: 'https://kutax-123.australia-southeast1.run.app',
  CANONICAL_HOST: 'kutax.com.au',
  ORIGIN_SHARED_SECRET: 'synthetic-origin-secret-for-tests-only-481',
};

/** Records what reaches Cloud Run and answers with the given response. */
function origin(response = new Response('ok')) {
  const seen = [];
  return { seen, fetch: async (request) => { seen.push(request); return response; } };
}

test('forwards to Cloud Run with the secret and the visitor address, replacing anything a client sent', async () => {
  const upstream = origin();
  const request = new Request('https://kutax.com.au/api/documents/?page=1', {
    headers: { 'CF-Connecting-IP': '203.0.113.7', 'X-KuTax-Origin-Verify': 'forged', 'X-KuTax-Client-IP': '198.51.100.1', Cookie: 'session=1' },
  });
  assert.equal((await proxyRequest(request, env, upstream.fetch)).status, 200);
  const sent = upstream.seen[0];
  assert.equal(sent.url, 'https://kutax-123.australia-southeast1.run.app/api/documents/?page=1');
  assert.equal(sent.headers.get('X-KuTax-Origin-Verify'), env.ORIGIN_SHARED_SECRET);
  assert.equal(sent.headers.get('X-KuTax-Client-IP'), '203.0.113.7');
  assert.equal(sent.headers.get('X-Forwarded-Host'), 'kutax.com.au');
  assert.equal(sent.headers.get('Cookie'), 'session=1');
  assert.equal(sent.redirect, 'manual');
});

test('sends www and other names to the one public address', async () => {
  const upstream = origin();
  const response = await proxyRequest(new Request('https://www.kutax.com.au/review?year=2025'), env, upstream.fetch);
  assert.equal(response.status, 301);
  assert.equal(response.headers.get('Location'), 'https://kutax.com.au/review?year=2025');
  assert.equal(upstream.seen.length, 0);
});

test('points redirects to the run.app address back at the public host', async () => {
  const upstream = origin(new Response(null, { status: 302, headers: { Location: 'https://kutax-123.australia-southeast1.run.app/summary' } }));
  const response = await proxyRequest(new Request('https://kutax.com.au/'), env, upstream.fetch);
  assert.equal(response.headers.get('Location'), 'https://kutax.com.au/summary');
});

test('refuses to run without a proper configuration, and reports an unreachable origin', async () => {
  assert.equal((await proxyRequest(new Request('https://kutax.com.au/'), { ...env, ORIGIN_SHARED_SECRET: 'short' })).status, 500);
  assert.equal((await proxyRequest(new Request('https://kutax.com.au/'), { ...env, ORIGIN_URL: 'http://kutax.example' })).status, 500);
  const failing = async () => { throw new TypeError('network'); };
  assert.equal((await proxyRequest(new Request('https://kutax.com.au/'), env, failing)).status, 502);
});

test('caches only hashed build files, never pages or the API', () => {
  const get = (path) => cachePolicyFor(new Request('https://kutax.com.au' + path), path);
  assert.ok(get('/main-TTYDZ34L.js'));
  assert.ok(get('/chunk-BOacaAMR.js'));
  assert.ok(get('/styles-T4PORNJA.css'));
  assert.ok(get('/media/dm-mono-latin-400-normal-ASEO4XL4.woff2'));
  assert.equal(get('/'), undefined);
  assert.equal(get('/index.html'), undefined);
  assert.equal(get('/favicon.svg'), undefined);
  assert.equal(get('/api/health/ready'), undefined);
  assert.equal(cachePolicyFor(new Request('https://kutax.com.au/main-A.js', { method: 'POST' }), '/main-A.js'), undefined);
});
