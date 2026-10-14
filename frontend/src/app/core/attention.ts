import { translate } from './i18n';

/**
 * Points the person at the field that stops them: scrolls it into view, focuses it, outlines it and shows the reason
 * beside it. The outline clears as soon as they change that field.
 */
export function showAttention(target: string | Element | null | undefined, reason: string) {
  const element = typeof target === 'string' ? document.getElementById(target) : target;
  if (!(element instanceof HTMLElement)) return false;
  document.querySelectorAll('.needs-attention').forEach((old) => clearAttention(old as HTMLElement));
  element.classList.add('needs-attention');
  element.setAttribute('aria-invalid', 'true');
  const note = document.createElement('span');
  note.className = 'attention-note';
  note.id = (element.id || 'field') + '-attention';
  note.textContent = translate(reason);
  note.setAttribute('role', 'alert');
  element.insertAdjacentElement('afterend', note);
  element.setAttribute('aria-describedby', note.id);
  const clear = () => clearAttention(element);
  element.addEventListener('input', clear, { once: true });
  element.addEventListener('change', clear, { once: true });
  const reduced = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  element.scrollIntoView?.({ behavior: reduced ? 'auto' : 'smooth', block: 'center' });
  element.focus({ preventScroll: true });
  return true;
}

export function clearAttention(element: HTMLElement) {
  element.classList.remove('needs-attention');
  element.removeAttribute('aria-invalid');
  const id = element.getAttribute('aria-describedby');
  if (id?.endsWith('-attention')) {
    document.getElementById(id)?.remove();
    element.removeAttribute('aria-describedby');
  }
}
