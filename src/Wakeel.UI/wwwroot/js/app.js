// الوكيل — مساعدات JS الصغيرة التي يستدعيها Blazor عبر JS interop.
window.wakeelUi = {
  /// Sets or clears data-theme on the document root. Pass null/undefined to follow the OS theme.
  setTheme(theme) {
    const root = document.documentElement;
    if (theme === 'light' || theme === 'dark') {
      root.setAttribute('data-theme', theme);
    } else {
      root.removeAttribute('data-theme');
    }
  },

  /// Binds Ctrl+F (or Cmd+F) globally to focus a WSearch instance instead of opening the
  /// WebView2/browser find bar. Only the most recently bound instance wins (last call replaces
  /// the previous listener), which matches the common case of one primary local-search box per page.
  /// `token` identifies the calling WSearch instance so a later unbindCtrlF call from a component
  /// that has since been disposed and replaced cannot tear down a newer instance's binding.
  bindCtrlF(dotNetRef, token) {
    if (window.__wakeelCtrlFHandler) {
      window.removeEventListener('keydown', window.__wakeelCtrlFHandler);
    }
    const handler = (e) => {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) {
        e.preventDefault();
        dotNetRef.invokeMethodAsync('FocusInputAsync');
      }
    };
    window.__wakeelCtrlFHandler = handler;
    window.__wakeelCtrlFToken = token;
    window.addEventListener('keydown', handler);
  },

  /// Releases the global Ctrl+F binding installed by bindCtrlF, but only when `token` still matches
  /// the instance that currently owns it (a later bindCtrlF call from a different WSearch already
  /// replaced it, in which case this is a no-op).
  unbindCtrlF(token) {
    if (window.__wakeelCtrlFHandler && window.__wakeelCtrlFToken === token) {
      window.removeEventListener('keydown', window.__wakeelCtrlFHandler);
      window.__wakeelCtrlFHandler = null;
      window.__wakeelCtrlFToken = null;
    }
  },

  /// Traps focus inside an open WDialog and routes Esc back to Blazor. Only one dialog is ever
  /// open at a time in this app (WDialog is modal), so a single module-level handler is enough;
  /// a later call replaces the previous one. Remembers the element that had focus before the
  /// dialog opened so releaseDialog can restore it.
  trapDialog(dialogEl, dotNetRef) {
    if (window.__wakeelDialogHandler) {
      window.removeEventListener('keydown', window.__wakeelDialogHandler);
    }
    if (!dialogEl) {
      return;
    }
    window.__wakeelDialogRestore = document.activeElement;
    const focusable = () =>
      Array.from(dialogEl.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'))
        .filter((el) => !el.disabled && el.offsetParent !== null);

    const first = focusable()[0];
    if (first) {
      first.focus();
    } else {
      dialogEl.focus();
    }

    const handler = (e) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        dotNetRef.invokeMethodAsync('HandleEscapeAsync');
        return;
      }
      if (e.key !== 'Tab') {
        return;
      }
      const items = focusable();
      if (items.length === 0) {
        return;
      }
      const firstItem = items[0];
      const lastItem = items[items.length - 1];
      if (e.shiftKey && document.activeElement === firstItem) {
        e.preventDefault();
        lastItem.focus();
      } else if (!e.shiftKey && document.activeElement === lastItem) {
        e.preventDefault();
        firstItem.focus();
      }
    };
    window.__wakeelDialogHandler = handler;
    window.addEventListener('keydown', handler);
  },

  /// Releases the focus trap installed by trapDialog (keydown listener) and restores focus to the
  /// element that had it before the dialog opened. Called when the dialog closes for any reason
  /// (confirm, cancel, close button, Esc, scrim click) and from WDialog's DisposeAsync.
  releaseDialog() {
    if (window.__wakeelDialogHandler) {
      window.removeEventListener('keydown', window.__wakeelDialogHandler);
      window.__wakeelDialogHandler = null;
    }
    if (window.__wakeelDialogRestore) {
      window.__wakeelDialogRestore.focus?.();
      window.__wakeelDialogRestore = null;
    }
  },
};
