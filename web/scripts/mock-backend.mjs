/**
 * 本地 mock 后端：完全按照 Go 后端（server/internal/api）的契约实现，
 * 用于在没有 Go 环境的机器上联调前端。
 *
 *   node scripts/mock-backend.mjs          # 默认监听 8080
 *   PORT=9000 node scripts/mock-backend.mjs
 *   MOCK_AUTO_REPORT=1 node scripts/mock-backend.mjs   # 每 20 秒模拟一条新上报
 *
 * 覆盖的接口（见 docs/API_CONTRACT.md）：
 *   GET    /api/health
 *   GET    /api/issues
 *   POST   /api/issues                 （并向 WebSocket 广播 issue.created）
 *   PATCH  /api/issues/{id}/status     （广播 issue.updated）
 *   DELETE /api/issues/{id}            （广播 issue.deleted）
 *   POST   /api/analysis/triage        （本地算法分诊，不调用任何大模型）
 *   GET    /ws                          （WebSocket 实时推送）
 */
import { createServer } from 'node:http';
import { createHash } from 'node:crypto';

const PORT = Number(process.env.PORT ?? 8080);
const AUTO_REPORT = process.env.MOCK_AUTO_REPORT === '1';

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
  {
    id: 'ISS-1792988510000-ZYXW987654321',
    title: '地下车库排水沟返味',
    description: 'B1 停车场排水沟有异味，怀疑堵塞',
    priority: 'low',
    status: 'resolved',
    position: { x: -8.4, y: -0.2, z: 12.7 },
    createdAt: '2026-10-24T02:11:00Z',
    updatedAt: '2026-10-24T08:30:00Z',
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

/* ------------------------------------------------------------------ *
 *  WebSocket（RFC 6455 手写实现，不引入任何依赖）
 * ------------------------------------------------------------------ */

const WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';
const sockets = new Set();

function encodeFrame(payload) {
  const data = Buffer.from(payload, 'utf8');
  const length = data.length;
  let header;
  if (length < 126) {
    header = Buffer.from([0x81, length]);
  } else if (length < 65536) {
    header = Buffer.alloc(4);
    header[0] = 0x81;
    header[1] = 126;
    header.writeUInt16BE(length, 2);
  } else {
    header = Buffer.alloc(10);
    header[0] = 0x81;
    header[1] = 127;
    header.writeBigUInt64BE(BigInt(length), 2);
  }
  return Buffer.concat([header, data]);
}

function broadcast(type, data) {
  const message = JSON.stringify({ type, data });
  for (const socket of sockets) {
    try {
      socket.write(encodeFrame(message));
    } catch {
      sockets.delete(socket);
    }
  }
}

function decodeFrames(buffer) {
  // 返回 { messages: string[], rest: Buffer }
  const messages = [];
  let offset = 0;
  while (offset + 2 <= buffer.length) {
    const first = buffer[offset];
    const second = buffer[offset + 1];
    const opcode = first & 0x0f;
    const masked = (second & 0x80) === 0x80;
    let length = second & 0x7f;
    let cursor = offset + 2;
    if (length === 126) {
      if (cursor + 2 > buffer.length) break;
      length = buffer.readUInt16BE(cursor);
      cursor += 2;
    } else if (length === 127) {
      if (cursor + 8 > buffer.length) break;
      length = Number(buffer.readBigUInt64BE(cursor));
      cursor += 8;
    }
    let mask = null;
    if (masked) {
      if (cursor + 4 > buffer.length) break;
      mask = buffer.subarray(cursor, cursor + 4);
      cursor += 4;
    }
    if (cursor + length > buffer.length) break;
    const payload = Buffer.from(buffer.subarray(cursor, cursor + length));
    if (mask) {
      for (let i = 0; i < payload.length; i += 1) {
        payload[i] ^= mask[i % 4];
      }
    }
    cursor += length;
    offset = cursor;

    if (opcode === 0x8) {
      messages.push('__close__');
    } else if (opcode === 0x9) {
      messages.push('__ping__');
    } else if (opcode === 0x1 || opcode === 0x2) {
      messages.push(payload.toString('utf8'));
    }
  }
  return { messages, rest: buffer.subarray(offset) };
}

function handleUpgrade(req, socket) {
  const key = req.headers['sec-websocket-key'];
  const accept = createHash('sha1').update(key + WS_GUID).digest('base64');
  socket.write(
    'HTTP/1.1 101 Switching Protocols\r\n' +
      'Upgrade: websocket\r\n' +
      'Connection: Upgrade\r\n' +
      `Sec-WebSocket-Accept: ${accept}\r\n\r\n`,
  );

  sockets.add(socket);

  let buffer = Buffer.alloc(0);
  socket.on('data', (chunk) => {
    buffer = Buffer.concat([buffer, chunk]);
    const { messages, rest } = decodeFrames(buffer);
    buffer = rest;
    for (const message of messages) {
      if (message === '__close__') {
        socket.end();
        return;
      }
      if (message === '__ping__') {
        continue;
      }
      try {
        const parsed = JSON.parse(message);
        if (parsed?.type === 'ping') {
          socket.write(encodeFrame(JSON.stringify({ type: 'pong', data: {} })));
        }
      } catch {
        // 忽略非法消息
      }
    }
  });

  socket.on('close', () => sockets.delete(socket));
  socket.on('error', () => sockets.delete(socket));

  socket.write(
    encodeFrame(
      JSON.stringify({
        type: 'hello',
        data: { serverTime: now(), issues: issues.length },
      }),
    ),
  );
}

/* ------------------------------------------------------------------ *
 *  本地分诊算法（2-gram 关键词 + Jaccard 相似度 + 3D 距离 + 规则表）
 * ------------------------------------------------------------------ */

const CATEGORY_RULES = [
  { category: '渗漏', keywords: ['渗水', '漏水', '水渍', '积水', '渗漏', '发黑'] },
  { category: '消防', keywords: ['消防', '灭火器', '喷淋', '烟感', '防火'] },
  { category: '电气', keywords: ['配电箱', '电箱', '漏电', '插座', '线路', '跳闸'] },
  { category: '结构', keywords: ['裂缝', '塌陷', '承重', '结构', '梁', '地基'] },
  { category: '设备', keywords: ['设备', '电梯', '空调', '风机', '水泵', '锈蚀'] },
  { category: '破损', keywords: ['破损', '碎裂', '玻璃', '缺损', '墙体'] },
];

function bigrams(text) {
  const clean = String(text ?? '').replace(/\s+/g, '');
  const result = new Set();
  for (let i = 0; i < clean.length - 1; i += 1) {
    result.add(clean.slice(i, i + 2));
  }
  if (clean.length === 1) {
    result.add(clean);
  }
  return result;
}

function jaccard(a, b) {
  if (a.size === 0 || b.size === 0) return 0;
  let inter = 0;
  for (const item of a) {
    if (b.has(item)) inter += 1;
  }
  return inter / (a.size + b.size - inter);
}

function extractKeywords(title, description) {
  const text = `${title ?? ''}${description ?? ''}`;
  const hits = [];
  for (const rule of CATEGORY_RULES) {
    for (const keyword of rule.keywords) {
      if (text.includes(keyword) && !hits.includes(keyword)) {
        hits.push(keyword);
      }
    }
  }
  if (hits.length === 0) {
    for (const gram of bigrams(title ?? '')) {
      if (hits.length >= 3) break;
      hits.push(gram);
    }
  }
  return hits.slice(0, 5);
}

function classify(text) {
  for (const rule of CATEGORY_RULES) {
    if (rule.keywords.some((keyword) => text.includes(keyword))) {
      return rule.category;
    }
  }
  return '其他';
}

function distance(a, b) {
  const dx = (a?.x ?? 0) - (b?.x ?? 0);
  const dy = (a?.y ?? 0) - (b?.y ?? 0);
  const dz = (a?.z ?? 0) - (b?.z ?? 0);
  return Math.sqrt(dx * dx + dy * dy + dz * dz);
}

function triage(payload) {
  const title = String(payload?.title ?? '');
  const description = String(payload?.description ?? '');
  const position = payload?.position ?? { x: 0, y: 0, z: 0 };
  const text = `${title}${description}`;
  const category = classify(text);
  const keywords = extractKeywords(title, description);
  const reasons = [];
  const similarIssues = [];

  for (const issue of issues) {
    const similarity = jaccard(bigrams(text), bigrams(`${issue.title}${issue.description}`));
    const d = distance(position, issue.position);
    const score = similarity * 0.75 + (d < 5 ? 0.25 : 0);
    if (score > 0.15) {
      similarIssues.push({
        id: issue.id,
        title: issue.title,
        similarity: Math.round(similarity * 100) / 100,
        distance: Math.round(d * 10) / 10,
        ageDays: Math.max(
          0,
          Math.round((Date.now() - new Date(issue.createdAt).getTime()) / 86400000),
        ),
        status: issue.status,
        advice:
          d < 5
            ? '同一位置附近已上报过，建议先确认是否重复报修'
            : '文本相似但距离较远，可能是同类问题',
      });
    }
  }
  similarIssues.sort((a, b) => b.similarity - a.similarity);

  let suggestedPriority = 'medium';
  if (['渗漏', '消防', '结构'].includes(category)) {
    suggestedPriority = 'high';
    reasons.push(`类别「${category}」历史同类问题多定级为 high`);
  } else if (['电气', '设备'].includes(category)) {
    suggestedPriority = 'medium';
    reasons.push(`类别「${category}」建议先处理再观察`);
  } else {
    suggestedPriority = 'low';
    reasons.push(`未命中高危关键词，建议按常规巡检排期`);
  }
  if (keywords.length > 0) {
    reasons.push(`命中关键词「${keywords.join('/')}」`);
  }

  const top = similarIssues[0];
  let duplicateRisk = 'none';
  if (top && top.similarity >= 0.4 && top.distance <= 5) {
    duplicateRisk = 'high';
    reasons.push(
      `${top.ageDays} 天前在相距 ${top.distance.toFixed(1)} 米处已上报过类似问题，可能重复`,
    );
  } else if (similarIssues.length > 0) {
    duplicateRisk = 'low';
    reasons.push('存在相似历史问题，但距离较远，重复可能性较低');
  }

  const confidence = Math.min(
    0.95,
    0.45 + keywords.length * 0.08 + (duplicateRisk === 'high' ? 0.12 : 0),
  );

  return {
    suggestedPriority,
    suggestedCategory: category,
    confidence: Math.round(confidence * 100) / 100,
    duplicateRisk,
    keywords,
    reasons,
    similarIssues: similarIssues.slice(0, 3),
  };
}

/* ------------------------------------------------------------------ *
 *  HTTP 服务
 * ------------------------------------------------------------------ */

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

  if (req.method === 'POST' && path === '/api/issues') {
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
      if (!payload?.title) {
        sendError(res, 400, 'invalid_argument', '标题(title)不能为空', 'title');
        return;
      }
      const issue = {
        id: `ISS-${Date.now()}-MOCK${String(issues.length).padStart(4, '0')}`,
        title: String(payload.title),
        description: String(payload.description ?? ''),
        priority: ['low', 'medium', 'high'].includes(payload.priority) ? payload.priority : 'medium',
        status: 'open',
        position: {
          x: Number(payload.position?.x ?? 0),
          y: Number(payload.position?.y ?? 0),
          z: Number(payload.position?.z ?? 0),
        },
        createdAt: now(),
        updatedAt: now(),
      };
      issues = [issue, ...issues];
      broadcast('issue.created', issue);
      send(res, 201, { issue });
    });
    return;
  }

  if (req.method === 'POST' && path === '/api/analysis/triage') {
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
      send(res, 200, triage(payload));
    });
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
      broadcast('issue.updated', issues[index]);
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
    broadcast('issue.deleted', { id });
    send(res, 200, { deleted: id });
    return;
  }

  sendError(res, 404, 'not_found', `未知接口: ${path}`, '');
});

server.on('upgrade', (req, socket) => {
  const url = new URL(req.url ?? '/', `http://${req.headers.host}`);
  if (url.pathname === '/ws' || url.pathname === '/api/ws') {
    handleUpgrade(req, socket);
    return;
  }
  socket.destroy();
});

// 服务端心跳：每 25 秒 ping 一次
setInterval(() => {
  broadcast('ping', {});
}, 25000);

// 可选：模拟巡检员不断上报
if (AUTO_REPORT) {
  const samples = [
    { title: '东侧外墙渗水', description: '墙皮发黑，有水渍往下淌', priority: 'high', position: { x: 12.4, y: 0.0, z: -3.1 } },
    { title: '三楼走廊灯管闪烁', description: '日光灯管接触不良', priority: 'medium', position: { x: -2.1, y: 2.6, z: 7.8 } },
    { title: '南门消防栓被遮挡', description: '货架挡住了消防栓', priority: 'high', position: { x: 5.6, y: 0.4, z: -9.2 } },
  ];
  let counter = 0;
  setInterval(() => {
    const sample = samples[counter % samples.length];
    counter += 1;
    const issue = {
      id: `ISS-${Date.now()}-AUTO${String(counter).padStart(4, '0')}`,
      ...sample,
      status: 'open',
      createdAt: now(),
      updatedAt: now(),
    };
    issues = [issue, ...issues];
    broadcast('issue.created', issue);
  }, 20000);
}

server.listen(PORT, '127.0.0.1', () => {
  console.log(`mock backend listening on http://127.0.0.1:${PORT}`);
  console.log(`mock websocket endpoint: ws://127.0.0.1:${PORT}/ws`);
});