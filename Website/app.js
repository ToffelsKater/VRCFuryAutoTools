// No Scriban here on purpose: all dynamic data lives in index.html (data-listing-url).
const listingUrl = document.body.dataset.listingUrl;

document.getElementById('addToVcc').addEventListener('click', () => {
  window.location.assign(`vcc://vpm/addRepo?url=${encodeURIComponent(listingUrl)}`);
});

const copyButton = document.getElementById('copyUrl');
const copyLabel = copyButton.querySelector('span');
copyButton.addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(listingUrl);
    copyLabel.textContent = 'Copied';
  } catch {
    // Clipboard API blocked: select the URL so Ctrl+C works.
    getSelection().selectAllChildren(document.getElementById('listingUrl'));
    copyLabel.textContent = 'Press Ctrl+C';
  }
  copyButton.classList.add('copied');
  setTimeout(() => {
    copyLabel.textContent = 'Copy';
    copyButton.classList.remove('copied');
  }, 1600);
});

const emptyState = document.getElementById('emptyState');
document.getElementById('search').addEventListener('input', ({ target: { value } }) => {
  const q = value.trim().toLowerCase();
  let shown = 0;
  document.querySelectorAll('#packageList .package').forEach(p => {
    p.hidden = q !== '' && !p.dataset.search.toLowerCase().includes(q);
    if (!p.hidden) shown++;
  });
  emptyState.hidden = shown > 0;
});
