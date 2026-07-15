const root = document.documentElement;
const stored = localStorage.getItem('lmm-site-theme');
const preferred = matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
root.dataset.theme = stored || preferred;

document.querySelector('.theme-toggle').addEventListener('click', () => {
  root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
  localStorage.setItem('lmm-site-theme', root.dataset.theme);
});

const observer = new IntersectionObserver(entries => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      observer.unobserve(entry.target);
    }
  });
}, { threshold: 0.12 });

document.querySelectorAll('.reveal').forEach(element => observer.observe(element));

const pagesMatch = location.hostname.match(/^([^.]+)\.github\.io$/i);
const pathPart = location.pathname.split('/').filter(Boolean)[0];
if (pagesMatch && pathPart) {
  const repository = `https://github.com/${pagesMatch[1]}/${pathPart}`;
  document.querySelectorAll('[data-repo-link]').forEach(link => link.href = repository);
  document.querySelectorAll('[data-release-link]').forEach(link => link.href = `${repository}/releases/latest`);
}
