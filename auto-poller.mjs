import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { discover, publicCatalog, readJson } from './accounts.mjs';
import { applyRecharge } from './recharge.mjs';
const ROOT = path.dirname(fileURLToPath(import.meta.url)), DATA = path.join(ROOT, 'data');
const clamp = n => Math.max(0, Math.min(1, n));
const number = n => n !== null && n !== undefined && n !== '' && Number.isFinite(Number(n));
export function moneyView(left, total, currency = 'CNY') {
 if (!number(left)) throw new Error('余额格式异常');
 const known = number(total) && Number(total) > 0;
 const symbol = currency === 'CNY' ? '¥' : currency === 'USD' ? '$' : currency === 'quota' ? '' : currency + ' ';
 return { key: 'balance', label: '余额', ratio: known ? clamp(Number(left) / Number(total)) : null, text: symbol + Number(left).toFixed(2) + (currency === 'quota' ? ' 额度' : '') };
}
function percent(window, key, label) {
 const used = window?.used_percent ?? window?.usedPercent ?? window?.used_percentage ?? window?.utilization;
 if (!number(used) || Number(used) < 0 || Number(used) > 100) return null;
 const ratio = clamp(1 - Number(used) / 100);
 return { key, label, ratio, text: `${Math.round(ratio * 100)}%`, resetsAt: window.resets_at || window.resetsAt || null };
}
export function subscriptionViews(limits, claude = false) {
 return [percent(claude ? limits?.five_hour : limits?.primary, '5h', '5 小时'), percent(claude ? limits?.seven_day : limits?.secondary, 'week', '每周')].filter(Boolean);
}
async function getJson(url, headers = {}) {
 const res = await fetch(url, { headers, redirect: 'error', signal: AbortSignal.timeout(12000) });
 if (!res.ok) {
  const e = new Error(res.status === 401 || res.status === 403 ? '登录已过期或权限不足，请在原客户端更新' : res.status === 429 ? '请求频繁，稍后再试' : `服务响应 HTTP ${res.status}`);
  e.status = res.status; throw e;
 }
 return res.json();
}
export function sub2View(j) {
 const b = j?.data && typeof j.data === 'object' ? j.data : j;
 if (b?.isValid === false) throw new Error('登录凭据已失效');
 const remaining = number(b?.remaining) ? Number(b.remaining) : number(b?.balance) ? Number(b.balance) : null;
 if (remaining === null) throw new Error('接口未提供余额');
 const spent = b.usage?.total?.actual_cost;
 const currency = b.unit || 'USD';
 return { status: 'ok', source: '中转站 · 实时账户余额', views: [moneyView(remaining, null, currency)], wallet: { balance: remaining, spent: number(spent) && Number(spent) >= 0 ? Number(spent) : null, currency, scope: 'account-sub2' }, detail: '实时账户余额' };
}
export function newApiView(j, status = {}) {
 const b = j?.data;
 if (j?.success === false || !b || typeof b !== 'object') throw new Error('接口未提供有效额度');
 const q = b.user_quota || b, account = !!b.user_quota;
 if (!account && b.unlimited_quota) return { status: 'ok', source: '中转站 · 令牌额度', views: [{ key: 'balance', label: '令牌额度', ratio: null, text: '不限额' }], detail: '此令牌不限额；服务未开放账户钱包余额' };
 if (!number(q.total_available)) throw new Error('接口未提供余额');
 const left = Number(q.total_available), used = number(q.total_used) ? Number(q.total_used) : null;
 const unit = Number(status.quota_per_unit);
 const display = status.quota_display_type || status.display_in_currency;
 // Integer quota is converted only when the service explicitly supplies its unit.
 const usd = display === 'USD';
 const view = unit > 0 ? moneyView(left / unit, null, usd ? 'USD' : 'quota') : {
  key: 'balance', label: '额度', text: left.toLocaleString('zh-CN') + ' 额度', ratio: null
 };
 const scale = unit > 0 ? unit : 1;
 return { status: 'ok', source: account ? '中转站 · 账户额度' : '中转站 · 令牌额度', views: [view], wallet: { balance: left / scale, spent: used === null ? null : used / scale, currency: unit > 0 && usd ? 'USD' : 'quota', scope: (account ? 'account-newapi-' : 'token-newapi-') + scale }, detail: account ? '账户剩余额度' : '当前令牌剩余额度（服务未开放账户钱包余额）' };
}
async function relay(a) {
 const headers = { Authorization: `Bearer ${a.key}`, Accept: 'application/json' };
 const base = a.base.replace(/\/+$/, ''), origin = new URL(base).origin;
 const usage = /\/v1$/.test(base) ? base + '/usage' : base + '/v1/usage';
 try { return sub2View(await getJson(usage, headers)); }
 catch (e) { if (e.status !== 404 && e.status !== 405) throw e; }
 const [u, s] = await Promise.allSettled([getJson(origin + '/api/usage/token/', headers), getJson(origin + '/api/status')]);
 if (u.status === 'rejected') {
  const e = u.reason;
  if (e.status === 404 || e.status === 405) return { status: 'limited', source: '已识别本机账户', detail: '这家中转站未开放已支持的余额接口', views: [] };
  throw e;
 }
 return newApiView(u.value, s.status === 'fulfilled' ? s.value?.data || {} : {});
}
async function deepseek(a) {
 if (a.dshToken) {
  try {
   const j = await getJson('https://platform.deepseek.com/api/v0/users/get_user_summary', { 'x-dsh-auth-token': a.dshToken, accept: 'application/json' });
   const b = j?.data?.biz_data;
   if (!b || !Array.isArray(b.normal_wallets)) throw new Error('钱包响应格式发生变化');
   const wallets = [...b.normal_wallets, ...(b.bonus_wallets || [])];
   if (wallets.some(x => !number(x.balance))) throw new Error('余额格式异常');
   const left = wallets.reduce((s, x) => s + Number(x.balance), 0);
   const costs = b.total_costs;
   const spent = Array.isArray(costs) && costs.every(x => number(x.amount)) ? costs.reduce((s, x) => s + Number(x.amount), 0) : null;
   return { status: 'ok', source: 'DSH 钱包 · 实时余额', views: [moneyView(left, null)], wallet: { balance: left, spent, currency: 'CNY', scope: 'deepseek-wallet' }, detail: '实时余额，含可用赠金' };
  } catch (e) { if (!a.key) throw e; }
 }
 const j = await getJson('https://api.deepseek.com/user/balance', { Authorization: `Bearer ${a.key}` });
 const b = j.balance_infos?.find(x => x.currency === 'CNY') || j.balance_infos?.[0];
 if (!b) throw new Error('接口未返回余额');
 return { status: 'ok', source: 'DeepSeek 官方 API', views: [moneyView(b.total_balance, null, b.currency)], wallet: { balance: Number(b.total_balance), spent: null, currency: b.currency, scope: 'deepseek-wallet' }, detail: '实时余额，含可用赠金' };
}
async function openai(a) {
 if (!a.admin) return { status: 'limited', source: '已识别 OpenAI API 账户', views: [], detail: '官方普通 API Key 不提供余额查询；检测到组织管理凭据后会自动读取费用' };
 const now = new Date(), start = Math.floor(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1) / 1000);
 let spend = 0, page;
 for (let i = 0; i < 100; i++) {
  const u = new URL('https://api.openai.com/v1/organization/costs');
  u.searchParams.set('start_time', String(start)); u.searchParams.set('limit', '31'); if (page) u.searchParams.set('page', page);
  const j = await getJson(u, { Authorization: `Bearer ${a.key}` });
  for (const bucket of j.data || []) for (const r of bucket.results || []) {
   if ((r.amount?.currency || 'usd').toLowerCase() !== 'usd' || !number(r.amount?.value)) throw new Error('费用格式异常');
   spend += Number(r.amount.value);
  }
  if (!j.has_more) {
   const hasBudget = Number(a.quotaTotal) > 0;
   return { status: 'ok', source: 'OpenAI 官方 · 本月费用', views: [hasBudget ? moneyView(Number(a.quotaTotal) - spend, a.quotaTotal, 'USD') : { key: 'cost', label: '本月已用', ratio: null, text: '$' + spend.toFixed(2) }], detail: `本月已用 $${spend.toFixed(2)} · ${hasBudget ? '进度显示本地已保存的月度预算余量' : '费用统计；官方未开放预付余额接口'}` };
  }
  if (!j.next_page) throw new Error('费用分页数据不完整'); page = j.next_page;
 }
 throw new Error('费用暂未汇总完成');
}
function findCodex() {
 const base = path.join(os.homedir(), 'AppData', 'Local', 'OpenAI', 'Codex', 'bin');
 try {
  const candidates = fs.readdirSync(base).map(d => path.join(base, d, 'codex.exe')).filter(p => fs.existsSync(p));
  candidates.sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs); if (candidates.length) return candidates[0];
 } catch {}
 return 'codex';
}
function codexLimits(a) {
 return new Promise((resolve, reject) => {
  const child = spawn(findCodex(), ['app-server'], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env: { ...process.env, CODEX_HOME: a.codexDir } });
  let done = false, buffer = '';
  const finish = (err, value) => { if (done) return; done = true; clearTimeout(timer); child.stdin.end(); child.kill(); err ? reject(err) : resolve(value); };
  const timer = setTimeout(() => finish(new Error('请求订阅额度超时')), 18000);
  const send = o => { if (!done && !child.stdin.destroyed) child.stdin.write(JSON.stringify(o) + '\n'); };
  child.on('error', () => finish(new Error('未找到可用的 Codex 客户端')));
  child.on('exit', () => { if (!done) finish(new Error('Codex 额度查询暂不可用')); });
  child.stdin.on('error', () => finish(new Error('Codex 连接已结束'))); child.stderr.resume();
  child.stdout.on('data', chunk => {
   buffer += chunk;
   if (buffer.length > 2 * 1024 * 1024) return finish(new Error('Codex 响应格式异常'));
   let index;
   while ((index = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, index); buffer = buffer.slice(index + 1);
    let j; try { j = JSON.parse(line); } catch { continue; }
    if (j.error) return finish(new Error('Codex 登录已过期或额度暂不可用'));
    if (j.id === 1) { send({ method: 'initialized' }); send({ id: 2, method: 'account/read', params: { refreshToken: false } }); }
    if (j.id === 2) {
     if (j.result?.account?.type !== 'chatgpt') return finish(new Error('当前 Codex 使用 API 账户，没有订阅额度'));
     send({ id: 3, method: 'account/rateLimits/read', params: {} });
    }
    if (j.id === 3) finish(null, j.result?.rateLimitsByLimitId?.codex || j.result?.rateLimits);
   }
  });
  send({ id: 1, method: 'initialize', params: { clientInfo: { name: 'whale_pocket', title: 'Whale Pocket', version: '2.0' } } });
 });
}
async function subscription(a) {
 const isClaude = a.kind === 'claude';
 const limits = isClaude ? await getJson('https://api.anthropic.com/api/oauth/usage', { Authorization: `Bearer ${a.oauth}`, 'anthropic-beta': 'oauth-2025-04-20', 'User-Agent': 'claude-code/2.0' }) : await codexLimits(a);
 const views = subscriptionViews(limits, isClaude);
 if (!views.length) return { status: 'limited', source: a.display + ' 本机登录', detail: '已识别登录，服务暂未提供订阅额度', views: [] };
 return { status: 'ok', source: a.display + ' · 订阅额度', views, detail: views.map(v => `${v.label}剩余 ${v.text}${v.resetsAt ? ' · 重置 ' + new Date(typeof v.resetsAt === 'number' ? v.resetsAt * 1000 : v.resetsAt).toLocaleString('zh-CN') : ''}`).join('\n') };
}
export async function main() {
 fs.mkdirSync(DATA, { recursive: true });
 const config = readJson(path.join(ROOT, 'config.json'), { accounts: [] }), old = readJson(path.join(DATA, 'balances.json'), {});
 const found = await discover(), results = {};
 const rechargeFile = path.join(DATA, 'recharge-baselines.json');
 const recharge = readJson(rechargeFile, { version: 1, accounts: {} });
 recharge.accounts ||= {};
 await Promise.all(found.accounts.map(async a => {
  const prefs = config.accounts?.find(x => x.id === a.id) || {};
  try { results[a.id] = await (a.kind === 'relay' ? relay(a) : a.kind === 'deepseek' ? deepseek(a) : a.kind === 'openai' ? openai({ ...a, quotaTotal: prefs.quotaTotal }) : subscription(a)); results[a.id].observedAtMs = Date.now(); applyRecharge(results[a.id], a.id, recharge); }
  catch (e) {
   const detail = e.name === 'TimeoutError' || e.name === 'AbortError' ? '请求超时，稍后刷新' : /^(登录|服务|请求|接口|钱包|余额|费用|Codex|未找到)/.test(e.message) ? e.message : '暂时连接不上，稍后重新扫描';
   const prev = old.accounts?.[a.id];
   results[a.id] = prev?.views?.length ? { ...prev, status: 'stale', detail: detail + ' · 当前显示上次成功数据', cachedAtMs: prev.cachedAtMs || prev.observedAtMs || old.updatedAtMs } : { status: 'error', detail, views: [] };
  }
 }));
 const result = { updatedAtMs: Date.now(), catalog: publicCatalog(found.accounts), accounts: results, notices: found.notices };
 const rechargeTemp = rechargeFile + '.' + process.pid + '.tmp';
 fs.writeFileSync(rechargeTemp, JSON.stringify(recharge, null, 2)); fs.renameSync(rechargeTemp, rechargeFile);
 const out = path.join(DATA, 'balances.json'), temp = out + '.' + process.pid + '.tmp';
 fs.writeFileSync(temp, JSON.stringify(result, null, 2)); fs.renameSync(temp, out);
 console.log(result.catalog.map(a => `${a.display}: ${results[a.id].status}`).join('\n'));
 return result;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main().catch(() => { console.error('无法刷新数据'); process.exitCode = 1; });
