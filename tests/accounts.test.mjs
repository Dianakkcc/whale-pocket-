import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const { discover, publicCatalog, parseCodexConfig, validBase } = await import(pathToFileURL(path.join(root, 'accounts.mjs')));
const { sub2View, newApiView, moneyView, subscriptionViews } = await import(pathToFileURL(path.join(root, 'auto-poller.mjs')));
const fixture = fs.mkdtempSync(path.join(fs.mkdirSync(path.join(root, 'test-results'), { recursive: true }) || path.join(root, 'test-results'), 'accounts-'));
fs.mkdirSync(path.join(fixture, '.codex'), { recursive: true });
const text = 'model_provider = "custom"\n[model_providers.custom]\nbase_url = "https://relay.example/v1"\n[model_providers.deepseek]\nbase_url = "https://api.deepseek.com"';
fs.writeFileSync(path.join(fixture, '.codex', 'config.toml'), text);
fs.writeFileSync(path.join(fixture, '.codex', 'auth.json'), JSON.stringify({ OPENAI_API_KEY: 'test-local-credential-not-real' }));
let found = await discover({ homeDir: fixture, env: {}, ccRows: [] });
assert.equal(found.accounts.length, 1, 'Global auth must not leak to an inactive provider');
assert.equal(found.accounts[0].base, 'https://relay.example/v1');
found = await discover({ homeDir: fixture, env: {}, ccRows: [
 { app_type: 'codex', name: 'default', settings_config: JSON.stringify({ config: text, auth: { OPENAI_API_KEY: 'test-local-credential-not-real' } }) },
 { app_type: 'claude', settings_config: '{"env":{}}' },
 { app_type: 'codex', settings_config: { config: {}, auth: {} } }
] });
assert.equal(found.accounts.length, 1, 'Same account in two apps must be deduplicated');
assert.deepEqual(found.accounts[0].sources, ['Codex', 'CC Switch']);
assert(!JSON.stringify(publicCatalog(found.accounts)).includes('test-local-credential'));
assert(!JSON.stringify(publicCatalog(found.accounts)).includes('"key"'));
assert.equal(validBase('http://relay.example'), null);
assert.equal(validBase('https://u:p@relay.example'), null);
assert.equal(parseCodexConfig('[model_providers."a-b"]\nbase_url = "https://a.example" # comment').providers['a-b'].base_url, 'https://a.example');
assert.equal(sub2View({ remaining: 0, balance: 9, unit: 'USD', usage: { total: { actual_cost: 10, cost: 999 } } }).views[0].text, '$0.00');
assert.equal(sub2View({ remaining: 4, unit: 'USD', usage: { total: { actual_cost: 6, cost: 999 } } }).wallet.spent, 6);
assert.equal(sub2View({ remaining: -1, usage: { total: { actual_cost: 11 } } }).wallet.balance, -1);
assert.equal(sub2View({ balance: 5 }).views[0].ratio, null);
assert.throws(() => sub2View({ balance: null }));
assert.throws(() => sub2View({ isValid: false, balance: 5 }));
assert.throws(() => moneyView(null, 10));
const token = { success: true, data: { total_available: 1000, total_used: 3000 } };
assert.equal(newApiView(token, { quota_per_unit: 1000, quota_display_type: 'USD' }).views[0].text, '$1.00');
assert.equal(newApiView(token).views[0].ratio, null);
assert.equal(newApiView(token).wallet.spent, 3000);
assert(!newApiView(token, { quota_per_unit: 1000, display_in_currency: true }).views[0].text.startsWith('$'));
assert.equal(newApiView({ data: { unlimited_quota: true } }).views[0].ratio, null);
assert(newApiView({ data: { unlimited_quota: true, user_quota: { total_available: 5, total_used: 5 } } }).source.includes('账户'));
assert.equal(subscriptionViews({ primary: { usedPercent: 30 }, secondary: { usedPercent: 90 } })[0].ratio, .7);
assert.equal(subscriptionViews({ primary: { usedPercent: null } }).length, 0);
assert.equal(subscriptionViews({ five_hour: { utilization: 100 } }, true)[0].ratio, 0);
console.log('通过：凭据按域绑定、重复账户合并、模板忽略、公开数据脱敏、真实余额与令牌额度换算、订阅余量解析。');
