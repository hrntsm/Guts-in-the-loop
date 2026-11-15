// Local-only stand-in based on the official Images API edit request/response contract.
// Control and stats routes belong to this test fixture, not the OpenAI API.
const http = require('node:http');
const zlib = require('node:zlib');
const crypto = require('node:crypto');
let mode = 'success';
const requests = [];
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const name = Buffer.from(type), size = Buffer.alloc(4), crc = Buffer.alloc(4);
  size.writeUInt32BE(data.length); crc.writeUInt32BE(crc32(Buffer.concat([name, data])));
  return Buffer.concat([size, name, data, crc]);
}
const header = Buffer.alloc(13);
header.writeUInt32BE(1024, 0); header.writeUInt32BE(1024, 4); header[8] = 8; header[9] = 2;
const pixels = Buffer.alloc((1024 * 3 + 1) * 1024);
for (let y = 0; y < 1024; y++) for (let x = 0; x < 1024; x++) {
  const offset = y * 3073 + 1 + x * 3;
  pixels[offset] = Math.floor(x / 205) * 40;
  pixels[offset + 1] = Math.floor(y / 205) * 40;
  pixels[offset + 2] = 150;
}
const png = Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk('IHDR', header), chunk('IDAT', zlib.deflateSync(pixels)), chunk('IEND', Buffer.alloc(0))]).toString('base64');
async function readBody(req) {
  if (req.body !== undefined) return typeof req.body === 'string' ? JSON.parse(req.body) : req.body;
  let body = ''; for await (const part of req) body += part;
  return body ? JSON.parse(body) : {};
}
http.createServer(async (req, res) => {
  const path = new URL(req.url, 'http://localhost').pathname;
  function reply(code, body) { res.writeHead(code, {'Content-Type': 'application/json'}); res.end(JSON.stringify(body)); }
  try {
    if (path === '/control' && req.method === 'POST') { mode = (await readBody(req)).mode; return reply(200, {mode}); }
    if (path === '/stats' && req.method === 'GET') return reply(200, {requests});
    if (path !== '/v1/images/edits' || req.method !== 'POST') return reply(501, {error: {message: 'Not modeled by this fixture'}});
    const body = await readBody(req);
    const valid = body.model === 'gpt-image-2.6-flare' && body.n === 1 && body.size === '1024x1024'
      && body.quality === 'low' && body.output_format === 'png' && body.images?.length === 1
      && body.images[0].image_url?.startsWith('data:image/png;base64,') && typeof body.prompt === 'string' && body.prompt.length <= 32000;
    const referenceSha256 = valid ? crypto.createHash('sha256').update(Buffer.from(body.images[0].image_url.split(',')[1], 'base64')).digest('hex') : null;
    requests.push({prompt: body.prompt, valid, mode, referenceSha256});
    if (!valid) return reply(400, {error: {message: 'Invalid image edit contract'}});
    const scenario = mode; mode = 'success';
    if (scenario === 'rate-limit') return reply(429, {error: {message: 'Simulated rate limit'}});
    if (scenario === 'html-error') { res.writeHead(502, {'Content-Type':'text/html'}); return res.end('<h1>Simulated gateway error</h1>'); }
    if (scenario === 'slow') await new Promise(resolve => setTimeout(resolve, 1500));
    reply(200, {created: 0, data: [{b64_json: png}]});
  } catch { if (!res.headersSent) reply(400, {error: {message: 'Invalid test request'}}); }
}).listen(process.env.PORT || 4500);
