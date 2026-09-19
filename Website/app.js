// No Scriban here on purpose: all dynamic data lives in index.html (data-listing-url).
const listingUrl = document.body.dataset.listingUrl;

document.getElementById('addToVcc').addEventListener('click', () => {
  window.location.assign(`vcc://vpm/addRepo?url=${encodeURIComponent(listingUrl)}`);
});

const copyButton = document.getElementById('copyUrl');
copyButton.addEventListener('click', async () => {
  await navigator.clipboard.writeText(listingUrl);
  copyButton.textContent = 'Copied!';
  setTimeout(() => (copyButton.textContent = 'Copy listing URL'), 1200);
});

document.getElementById('search').addEventListener('input', ({ target: { value } }) => {
  const q = value.trim().toLowerCase();
  document.querySelectorAll('#packageList .package').forEach(p => {
    p.hidden = q !== '' && !p.dataset.search.toLowerCase().includes(q);
  });
});
