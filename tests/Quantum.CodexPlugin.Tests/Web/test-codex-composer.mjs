import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(
  new URL('../../../src/Quantum.CodexPlugin/wwwroot/codex-composer.js', import.meta.url),
  'utf8',
);
const { attach } = await import(
  'data:text/javascript;base64,' + Buffer.from(source).toString('base64')
);

function composer() {
  const input = new EventTarget();
  input.disabled = false;
  const send = {
    disabled: false,
    clicks: 0,
    click() {
      this.clicks++;
    },
  };
  const binding = attach({
    closest: () => ({ querySelector: (selector) => (selector === 'textarea' ? input : send) }),
  });
  const key = (props = {}) => {
    const event = new Event('keydown', { cancelable: true });
    Object.assign(event, { key: 'Enter', ...props });
    input.dispatchEvent(event);
    return event;
  };
  return { input, send, binding, key };
}

test('Enter sends once and prevents the textarea newline; Ctrl and Cmd shortcuts still send', () => {
  const { send, key } = composer();
  assert.equal(key().defaultPrevented, true);
  assert.equal(send.clicks, 1);
  key({ ctrlKey: true });
  key({ metaKey: true });
  assert.equal(send.clicks, 3);
  assert.equal(key({ repeat: true }).defaultPrevented, true);
  assert.equal(send.clicks, 3);
});

test('Shift+Enter and Alt+Enter keep native multiline editing', () => {
  const { send, key } = composer();
  assert.equal(key({ shiftKey: true }).defaultPrevented, false);
  assert.equal(key({ altKey: true }).defaultPrevented, false);
  assert.equal(key({ key: 'a' }).defaultPrevented, false);
  assert.equal(send.clicks, 0);
});

test('IME candidate confirmation never sends, including keyCode 229 fallback', () => {
  const { input, send, key } = composer();
  input.dispatchEvent(new Event('compositionstart'));
  assert.equal(key().defaultPrevented, false);
  input.dispatchEvent(new Event('compositionend'));
  assert.equal(key({ isComposing: true }).defaultPrevented, false);
  assert.equal(key({ keyCode: 229 }).defaultPrevented, false);
  assert.equal(send.clicks, 0);
  key();
  assert.equal(send.clicks, 1);
});

test('busy or empty drafts do not send, and disposing removes all listeners', () => {
  const { input, send, binding, key } = composer();
  send.disabled = true;
  assert.equal(key().defaultPrevented, true);
  send.disabled = false;
  input.disabled = true;
  key();
  assert.equal(send.clicks, 0);
  input.disabled = false;
  binding.dispose();
  assert.equal(key().defaultPrevented, false);
  assert.equal(send.clicks, 0);
});
