// Calibração: o mesmo laço de CPU no worker, para conferir se o throttling do CDP também atinge workers.
self.onmessage = (e) => {
  const n = e.data;
  const t0 = performance.now();
  let x = 0;
  for (let i = 0; i < n; i++) x = (x + Math.sqrt(i) * 1.0000001) % 1e9;
  self.postMessage({ ms: performance.now() - t0, x });
};
