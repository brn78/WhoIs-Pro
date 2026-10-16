// Tema chiaro o scuro come Windows (la finestra segue Impostazioni > Personalizzazione > Colori), prima di disegnare la
// pagina: niente lampo del tema sbagliato all'avvio.
(function () {
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  const apply = () => document.documentElement.setAttribute('data-theme', media.matches ? 'dark' : 'light');
  apply();
  media.addEventListener('change', apply);
})();
