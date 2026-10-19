import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import crypto from 'node:crypto';

export const readJson = (file, fallback = null) => {
 try { return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '')); } catch { return fallback; }
};
const readText = file => { try { return fs.readFileSync(file, 'utf8'); } catch { return ''; } };
const usable = key => typeof key === 'string' && key.length > 10 && !/^\$\{|your[-_]|sk-xxx|placeholder/i.test(key);

// Parse only scalar fields needed for discovery. No configuration or usage script is executed.
export function parseCodexConfig(text) {
 const result = { providers: {} }; let section = result;
 if (typeof text !== 'string') return result;
 for (const raw of text.replace(/^\uFEFF/, '').split(/\r?\n/)) {
  const line = raw.trim();
  if (line.startsWith('[')) {
   const m = line.match(/^\[model_providers\.(?:"([^"]+)"|'([^']+)'|([\w-]+))\]\s*(?:#.*)?$/);
   section = m ? (result.providers[m[1] || m[2] || m[3]] ||= {}) : {};
   continue;
  }
  const m = line.match(/^([\w-]+)\s*=\s*("(?:[^"\\]|\\.)*"|'[^']*'|true|false)\s*(?:#.*)?$/);
  if (!m) continue;
  try { section[m[1]] = m[2][0] === "'" ? m[2].slice(1, -1) : JSON.parse(m[2]); } catch {}
 }
 return result;
}

export function validBase(raw) {
 try {
  const u = new URL(raw);
  if (u.protocol !== 'https:' || u.username || u.password || u.search || u.hash) return null;
  return u.href.replace(/\/+$/, '');
 } catch { return null; }
}
const identity = (origin, key) => crypto.createHash('sha256').update(origin + '\0' + key).digest('hex').slice(0, 14);
function stationName(base, hint) {
 const host = new URL(base).hostname;
 if (host === 'momoapi.asia' || host.endsWith('.momoapi.asia')) return 'MoMo API';
 return hint && !/^(default|custom|openai|codex|自定义)$/i.test(hint) && hint.length < 24 ? hint : host.replace(/^api\./, '');
}

export async function discover({ homeDir = os.homedir(), env = process.env, ccRows = null } = {}) {
 const list = [], byCredential = new Map(), notices = [];
 const codexDir = env.CODEX_HOME || path.join(homeDir, '.codex');
 const claudeDir = env.CLAUDE_CONFIG_DIR || path.join(homeDir, '.claude');
 const add = a => {
  const identityKey = a.key ? identity(new URL(a.base).origin, a.key) : a.id;
  const prior = byCredential.get(identityKey);
  if (prior) { prior.sources = [...new Set([...prior.sources, ...a.sources])]; return prior; }
  byCredential.set(identityKey, a); list.push(a); return a;
 };
 const addApi = (base, key, source, hint) => {
  base = validBase(base); if (!base || !usable(key)) return;
  const host = new URL(base).hostname;
  const kind = host === 'api.deepseek.com' ? 'deepseek' : host === 'api.openai.com' ? 'openai' : 'relay';
  return add({ id: kind === 'relay' ? 'relay_' + identity(new URL(base).origin, key) : kind,
   kind, display: kind === 'deepseek' ? 'DeepSeek' : kind === 'openai' ? 'ChatGPT API' : stationName(base, hint),
   base, key, sources: [source], admin: kind === 'openai' && key.startsWith('sk-admin-') });
 };
 const ingestCodex = (text, auth, source, hint) => {
  const c = parseCodexConfig(text), active = c.model_provider || 'openai';
  for (const [id, p] of Object.entries(c.providers)) {
   // A global credential belongs only to the active provider. Never reuse it on other hosts.
   const key = p.experimental_bearer_token || (p.env_key ? env[p.env_key] : null) || (id === active ? auth?.OPENAI_API_KEY : null);
   addApi(p.base_url || (id === 'openai' ? 'https://api.openai.com/v1' : ''), key, source, p.name || hint);
  }
  if (active === 'openai' && !c.providers.openai) addApi('https://api.openai.com/v1', auth?.OPENAI_API_KEY || env.OPENAI_API_KEY, source);
 };
 const auth = readJson(path.join(codexDir, 'auth.json'), {});
 ingestCodex(readText(path.join(codexDir, 'config.toml')), auth, 'Codex');
 if (ccRows === null) {
  ccRows = [];
  const dbFile = path.join(homeDir, '.cc-switch', 'cc-switch.db');
  if (fs.existsSync(dbFile)) {
   let db;
   try { const { DatabaseSync } = await import('node:sqlite'); db = new DatabaseSync(dbFile, { readOnly: true }); ccRows = db.prepare('SELECT app_type, name, settings_config FROM providers').all(); }
   catch { notices.push('CC Switch 配置暂时无法读取'); }
   finally { db?.close(); }
  }
 }
 for (const row of ccRows) {
  let s; try { s = typeof row.settings_config === 'string' ? JSON.parse(row.settings_config) : row.settings_config; } catch { continue; }
  if (!s || typeof s !== 'object') continue;
  if (row.app_type === 'codex') ingestCodex(s.config, s.auth, 'CC Switch', row.name);
  if (row.app_type === 'claude') {
   const e = s.env || {};
   if (e.ANTHROPIC_BASE_URL) addApi(e.ANTHROPIC_BASE_URL, e.ANTHROPIC_AUTH_TOKEN || e.ANTHROPIC_API_KEY, 'CC Switch', row.name);
  }
 }
 addApi(env.OPENAI_BASE_URL || 'https://api.openai.com/v1', env.OPENAI_API_KEY, '环境配置');
 addApi('https://api.openai.com/v1', env.OPENAI_ADMIN_KEY || env.WHALE_OPENAI_KEY, '本机凭据');
 addApi('https://api.deepseek.com', env.DEEPSEEK_API_KEY || env.WHALE_DS_KEY, '本机凭据');
 const yaml = readText(path.join(homeDir, '.dsh', '.credentials.yaml'));
 const dshBlock = yaml.split(/^ {2}deepseek-account-platform\/default:\s*$/m)[1]?.split(/^ {2}\S/m)[0];
 const dshToken = dshBlock?.match(/^\s+token:\s*['"]?([^\s'"]+)['"]?\s*$/m)?.[1];
 if (dshToken) {
  let ds = list.find(a => a.kind === 'deepseek');
  if (!ds) ds = add({ id: 'deepseek', kind: 'deepseek', display: 'DeepSeek', sources: [] });
  ds.dshToken = dshToken; ds.sources = [...new Set(['DSH 钱包', ...ds.sources])];
 }
 if (auth.tokens?.access_token || auth.tokens?.refresh_token) add({ id: 'codex', kind: 'codex', display: 'Codex', codexDir, sources: ['Codex 登录'] });
 const claudeAuth = readJson(path.join(claudeDir, '.credentials.json'), {})?.claudeAiOauth;
 if (claudeAuth?.accessToken) add({ id: 'claude', kind: 'claude', display: 'Claude Code', oauth: claudeAuth.accessToken, sources: ['Claude Code 登录'] });
 const claudeSettings = readJson(path.join(claudeDir, 'settings.json'), {})?.env || {};
 if (claudeSettings.ANTHROPIC_BASE_URL) addApi(claudeSettings.ANTHROPIC_BASE_URL, claudeSettings.ANTHROPIC_AUTH_TOKEN || claudeSettings.ANTHROPIC_API_KEY, 'Claude Code');
 if (env.ANTHROPIC_BASE_URL) addApi(env.ANTHROPIC_BASE_URL, env.ANTHROPIC_AUTH_TOKEN || env.ANTHROPIC_API_KEY, '环境配置');
 // The UI has one official service slot; prefer a wallet/admin credential over a plain key.
 const unique = [];
 for (const a of list) {
  const same = unique.find(x => x.id === a.id);
  if (!same) unique.push(a);
  else { same.sources = [...new Set([...same.sources, ...a.sources])]; if (a.admin || a.dshToken) Object.assign(same, a, { sources: same.sources }); }
 }
 return { accounts: unique, notices };
}

export function publicCatalog(accounts) {
 return accounts.map(a => ({ id: a.id, kind: a.kind, display: a.display, source: a.sources.join(' · '), origin: a.base ? new URL(a.base).origin : '', detected: true }));
}
