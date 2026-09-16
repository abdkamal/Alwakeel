// مدير نظام الوكيل — the few small things the administration screens need from the page itself.
window.wakeelAdmin = {
  /// Puts text on this computer's clipboard (A01's «نسخ الرمز»). Returns false when the engine
  /// refuses, so the screen can say so in words instead of pretending it worked.
  async copyText(text) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      return false;
    }
  },

  /// Takes whatever was copied back off the clipboard (A01 calls this as the recovery sheet leaves
  /// the screen). A code left sitting in the clipboard — and in this computer's clipboard history —
  /// would outlive every other precaution taken with it, so it is overwritten with nothing.
  async clearCopiedText() {
    try {
      await navigator.clipboard.writeText('');
      return true;
    } catch {
      return false;
    }
  },

  /// Moves the keyboard to an element, so a screen that has just opened puts the person straight
  /// into the field it is asking about.
  focusElement(element) {
    if (element && typeof element.focus === 'function') {
      element.focus();
    }
  },

  /// A04's letter preview: makes the next print or PDF carry the letter alone, at the paper size
  /// the person chose. Everything else on the screen is made invisible rather than removed, so the
  /// page does not reflow and what comes out is exactly the sheet that was on screen. The rule is
  /// put in and taken out around the one call that needs it; nothing is left behind.
  beginLetterPrint(paperSize) {
    let style = document.getElementById('wakeel-letter-print');
    if (!style) {
      style = document.createElement('style');
      style.id = 'wakeel-letter-print';
      document.head.appendChild(style);
    }

    style.textContent =
      '@page { size: ' + (paperSize === 'A5' ? 'A5' : 'A4') + '; margin: 0; }\n' +
      '@media print {\n' +
      '  body * { visibility: hidden !important; }\n' +
      '  .a04-letter-sheet, .a04-letter-sheet * { visibility: visible !important; }\n' +
      '  .a04-letter-sheet { position: fixed; inset: 0; margin: 0; width: 100%; height: auto;\n' +
      '    box-shadow: none; border: 0; border-radius: 0; }\n' +
      '}';
    return true;
  },

  /// Takes that rule back out again.
  endLetterPrint() {
    document.getElementById('wakeel-letter-print')?.remove();
    return true;
  },
};
