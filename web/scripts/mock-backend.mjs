/**
 * 本地 mock 后端：完全按照 Go 后端（server/internal/api）的契约实现，
 * 用于在没有 Go 环境的机器上联调前端。
 *
 *   node scripts/mock-backend.mjs          # 默认监听 8080
 *   PORT=9000 node scripts/mock-backend.mjs
 */
import { createServer } from 'node:http';

const PORT = Number(process.env.PORT ?? 8080);

let issues = [
  {
    id: 'ISS-1792988466562-SAAEVOZKQGIAC',
    title: '入口墙面破损',
    description: '左侧墙体存在裂缝',
    priority: 'high',
    status: 'open',
    position: { x: 0.42, y: 0.03, z: 1.26 },
    createdAt: '2026-10-26T12:21:06.5628133+08:00',
    updatedAt: '2026-10-26T12:21:12.032014+08:00',
  },
  {
    id: 'ISS-1792988500000-ABCD1234EFGH',
    title: '配电箱外壳锈蚀',
    description: '配电箱右下角锈蚀，建议更换',
    priority: 'medium',
    status: 'in_progress',
    position: { x: -3.11, y: 1.02, z: 4.5 },
    createdAt: '2026-10-25T09:10:00Z',
    updatedAt: '2026-10-25T09:10:00Z',
  },
];

const now = () => new Date().toISOString();

function send(res, status, payload) {
  const body = JSON.stringify(payload);
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Methods': 'GET, POST, PUT, PATCH, DELETE, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization',
  });
  res.end(body);
}

function sendError(res, status, code, message, field = '') {
  send(res, status, { error: { code, message, field } });
}

const server = createServer((req, res) => {
  if (req.method === 'OPTIONS') {
    res.writeHead(204, {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Allow-Methods': 'GET, POST, PUT, PATCH, DELETE, OPTIONS',
      'Access-Control-Allow-Headers': 'Content-Type, Authorization',
    });
    res.end();
    return;
  }

  const url = new URL(req.url ?? '/', `http://${req.headers.host}`);
  const path = url.pathname;

  if (req.method === 'GET' && path === '/api/health') {
    send(res, 200, { status: 'ok', service: 'unity-ar-inspection', issues: issues.length });
    return;
  }

  if (req.method === 'GET' && path === '/api/issues') {
    send(res, 200, { issues, count: issues.length });
    return;
  }

  const statusMatch = /^\/api\/issues\/([^/]+)\/status$/.exec(path);
  if ((req.method === 'PATCH' || req.method === 'POST') && statusMatch) {
    const id = decodeURIComponent(statusMatch[1]);
    let raw = '';
    req.on('data', (chunk) => {
      raw += chunk;
    });
    req.on('end', () => {
      let payload;
      try {
        payload = JSON.parse(raw);
      } catch {
        sendError(res, 400, 'invalid_json', '请求体不是合法的 JSON', '');
        return;
      }
      if (!['open', 'in_progress', 'resolved'].includes(payload?.status)) {
        sendError(
          res,
          400,
          'invalid_argument',
          `状态(status)必须是 open、in_progress 或 resolved 之一，当前值: ${JSON.stringify(payload?.status)}`,
          'status',
        );
        return;
      }
      const index = issues.findIndex((item) => item.id === id);
      if (index === -1) {
        sendError(res, 404, 'not_found', `未找到 ID 为 ${id} 的问题`, 'id');
        return;
      }
      issues[index] = { ...issues[index], status: payload.status, updatedAt: now() };
      send(res, 200, { issue: issues[index] });
    });
    return;
  }

  const deleteMatch = /^\/api\/issues\/([^/]+)$/.exec(path);
  if (req.method === 'DELETE' && deleteMatch) {
    const id = decodeURIComponent(deleteMatch[1]);
    const index = issues.findIndex((item) => item.id === id);
    if (index === -1) {
      sendError(res, 404, 'not_found', `未找到 ID 为 ${id} 的问题`, 'id');
      return;
    }
    issues.splice(index, 1);
    send(res, 200, { deleted: id });
    return;
  }

  sendError(res, 404, 'not_found', `未知接口: ${path}`, '');
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`mock backend listening on http://127.0.0.1:${PORT}`);
});