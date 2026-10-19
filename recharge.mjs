const finite = value => value !== null && value !== undefined && value !== '' && Number.isFinite(Number(value));
const clamp = value => Math.max(0, Math.min(1, value));
const epsilon = 0.00001;

// Each baseline is a balance observed after a credit, never lifetime spending.
// A credit smaller than spending between polls is still detectable when cumulative spending exists.
export function updateRecharge(previous, wallet, now = Date.now()) {
 if (!wallet || !finite(wallet.balance)) throw new Error('余额格式异常');
 const balance = Number(wallet.balance), spent = finite(wallet.spent) ? Number(wallet.spent) : null;
 const compatible = previous && previous.scope === wallet.scope && previous.currency === wallet.currency;
 let state = compatible ? { ...previous } : { baseline: balance > 0 ? balance : null, reason: 'initial', baselineAtMs: now, scope: wallet.scope, currency: wallet.currency };
 let credited = false;
 if (compatible && finite(previous.lastBalance)) {
  const deltaBalance = balance - Number(previous.lastBalance);
  const hasComparableSpent = spent !== null && finite(previous.lastSpent) && spent >= Number(previous.lastSpent) - epsilon;
  const deltaSpent = hasComparableSpent ? spent - Number(previous.lastSpent) : 0;
  const credit = deltaBalance + deltaSpent;
  // A counter reset or source change cannot provide a reliable credit inference.
  credited = (hasComparableSpent ? credit : deltaBalance) > epsilon;
  if (credited && balance > 0) {
   state.baseline = balance; state.baselineAtMs = now; state.reason = 'credit-observed';
   state.lastCredit = Math.max(0, credit); state.creditObservedAtMs = now;
  }
 }
 if (!finite(state.baseline) || state.baseline <= 0) state.baseline = balance > 0 ? balance : null;
 state.lastBalance = balance; state.lastSpent = spent; state.observedAtMs = now;
 return { state, credited, ratio: state.baseline > 0 ? clamp(balance / state.baseline) : null };
}

export function applyRecharge(result, id, book, now = Date.now()) {
 if (!result.wallet || result.status !== 'ok') return result;
 const { state, ratio } = updateRecharge(book.accounts[id], result.wallet, now);
 book.accounts[id] = state;
 const view = result.views.find(v => v.key === 'balance');
 if (view) { view.ratio = ratio; view.baseline = state.baseline; view.baselineAtMs = state.baselineAtMs; }
 const prefix = state.currency === 'CNY' ? '¥' : state.currency === 'USD' ? '$' : '';
 const formatted = state.baseline > 0 ? prefix + state.baseline.toFixed(2) + (prefix ? '' : ' 额度') : '待余额大于 0';
 const meaning = state.reason === 'initial' ? '首次启用时的余额' : '最近检测到充值后的余额';
 result.detail = (result.detail ? result.detail + '\n' : '') + `进度：当前余额 / ${meaning} ${formatted}`;
 result.baselineReason = state.reason;
 return result;
}
