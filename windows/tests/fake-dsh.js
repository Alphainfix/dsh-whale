'use strict';
// Isolated fixture: all control files and logs are under the test working directory.
const fs = require('fs');
const http = require('http');
const port = Number(fs.readFileSync('port.txt', 'utf8'));
if (fs.existsSync('fail.txt')) { console.error('Error: fixture plugin failed'); process.exit(2); }
const token = 'fixture-' + process.pid;
process.on('SIGINT', () => { fs.writeFileSync('graceful-stop.txt', 'SIGINT'); process.exit(0); });
const server = http.createServer((req, res) => {
  if (req.url === '/?token=' + token) { res.writeHead(303, {'Set-Cookie': 'dsh=fixture; Path=/; HttpOnly', Location: '/'}); res.end(); return; }
  // Like dsh-balance-sidebar: an exact route DSH serves without its login, failures as {ok:false, message}.
  if (req.url === '/api/model-balance') { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify({ok: true, active: null, providers: {deepseek: {ok: true, label: 'DeepSeek', kind: 'prepaid', currency: 'CNY', remaining: 86.4, limit: null, providerIds: ['deepseek-official']}, codex: {ok: true, label: 'ChatGPT 5h 余量', kind: 'quota', currency: '%', remaining: 72, limit: 100, providerIds: ['codex']}, kimi: {ok: false, message: 'Kimi 用量接口超时', providerIds: ['kimi-coding']}}})); return; }
  if (!(req.headers.cookie || '').includes('dsh=fixture')) { res.writeHead(401); res.end('dsh web authentication required'); return; }
  res.end('fixture');
});
server.listen(port, '127.0.0.1', () => {
  console.log('http://127.0.0.1:' + port + '/?token=' + token);
  fs.writeFileSync('environment.json', JSON.stringify({mode: process.env.DSH_PERMISSION_MODE || '', path: process.argv[1]}));
});
