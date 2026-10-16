// Tema della pagina prima di disegnarla (niente lampo del tema sbagliato): "auto" segue Windows (Impostazioni >
// Personalizzazione > Colori), oppure chiaro o scuro a scelta (pagina Impostazioni). La scelta resta in localStorage
// per il prossimo avvio e nel file delle impostazioni del programma.
(function () {
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  let choice = 'auto';
  try { choice = localStorage.getItem('theme') || 'auto'; } catch { /* senza memoria locale */ }
  const apply = () => document.documentElement.setAttribute('data-theme', choice === 'auto' ? (media.matches ? 'dark' : 'light') : choice);
  apply();
  media.addEventListener('change', apply);
  window.setTheme = (value) => {
    choice = value === 'light' || value === 'dark' ? value : 'auto';
    try { localStorage.setItem('theme', choice); } catch { /* senza memoria locale */ }
    apply();
  };
  window.getTheme = () => choice;
})();
