export function attach(marker) {
  const composer = marker.closest('.composer, .drawer-composer');
  const input = composer.querySelector('textarea');
  let composing = false;
  const start = () => {
    composing = true;
  };
  const end = () => {
    composing = false;
  };
  const keydown = (event) => {
    if (
      event.key !== 'Enter' ||
      event.shiftKey ||
      event.altKey ||
      composing ||
      event.isComposing ||
      event.keyCode === 229
    )
      return;
    event.preventDefault();
    if (event.repeat) return;
    const send = composer.querySelector('.send-button');
    if (!input.disabled && send && !send.disabled) send.click();
  };
  input.addEventListener('compositionstart', start);
  input.addEventListener('compositionend', end);
  input.addEventListener('keydown', keydown);
  return {
    dispose() {
      input.removeEventListener('compositionstart', start);
      input.removeEventListener('compositionend', end);
      input.removeEventListener('keydown', keydown);
    },
  };
}
