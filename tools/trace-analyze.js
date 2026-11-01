// BUG-005: resume una carpeta de `net-test -Trace`: tiempos por etapa y qué tenían las órdenes lentas.
// Uso: node tools/trace-analyze.js <carpeta> [frecuencia_stopwatch]
const fs = require('fs');
const path = require('path');
const dir = process.argv[2];
const freq = Number(process.argv[3] || 10000000); // Stopwatch.Frequency en Windows
const ms = t => t * 1000 / freq;
const pct = (a, p) => a.length === 0 ? NaN : a[Math.min(a.length - 1, Math.floor(a.length * p / 100))];
const line = (name, a) => {
  a.sort((x, y) => x - y);
  return `${name.padEnd(26)} n=${String(a.length).padStart(5)} p50=${pct(a, 50).toFixed(0).padStart(5)} p90=${pct(a, 90).toFixed(0).padStart(5)} p99=${pct(a, 99).toFixed(0).padStart(5)} max=${(a[a.length - 1] ?? NaN).toFixed(0).padStart(5)}`;
};

const handled = new Map();
for (const l of fs.readFileSync(path.join(dir, 'server-commands.csv'), 'utf8').split('\n')) {
  const [key, id, ts] = l.trim().split(',');
  if (ts) handled.set(`${key}|${id}`, Number(ts));
}
const Channeled = 1, ReliableMerged = 2;
const up = [], down = [], total = [], unmatched = [];
const slow = { n: 0, upLoss: 0, downLoss: 0, noLoss: 0 };
const upClean = [], upLossy = [], downClean = [], downLossy = [];
const resendGap = { C: [], S: [] };
let droppedReliable = { C: 0, S: 0 }, sentReliable = { C: 0, S: 0 };
const spurious = { C: 0, S: 0 };

for (const file of fs.readdirSync(dir).filter(f => /^cmd\d+\.csv$/.test(f))) {
  const n = file.match(/\d+/)[0];
  const relayPath = path.join(dir, `relay${n}.csv`);
  const drops = { C: [], S: [] };
  if (fs.existsSync(relayPath)) {
    const pending = new Map();
    const seen = new Map();
    for (const l of fs.readFileSync(relayPath, 'utf8').split('\n')) {
      const f = l.trim().split(',');
      if (f.length < 7) continue;
      const [ts, side, dropped, prop, seq, chan] = [Number(f[0]), f[1], f[2] === '1', Number(f[3]), f[4], f[5]];
      if (prop !== Channeled && prop !== ReliableMerged) continue;
      if (chan !== '2') continue; // canal 0 fiable y ordenado: identificador 0*4 + ReliableOrdered en LiteNetLib 2.1.5
      sentReliable[side]++;
      const k = `${side}|${seq}`;
      if (pending.has(k)) {
        resendGap[side].push(ms(ts - pending.get(k)));
        pending.delete(k);
      } else if (seen.has(k) && ts - seen.get(k) < freq * 5) {
        // Ya había llegado una copia: reenvío sin pérdida real, tráfico desperdiciado.
        spurious[side]++;
      }
      seen.set(k, ts);
      if (dropped) {
        droppedReliable[side]++;
        drops[side].push(ts);
        pending.set(k, ts);
      }
    }
  }
  const hasDrop = (side, from, to) => drops[side].some(t => t >= from && t <= to);
  for (const l of fs.readFileSync(path.join(dir, file), 'utf8').split('\n')) {
    const [key, id, sent, recv] = l.trim().split(',');
    if (!recv) continue;
    const at = handled.get(`${key}|${id}`);
    const all = ms(Number(recv) - Number(sent));
    total.push(all);
    if (at === undefined) { unmatched.push(all); continue; }
    const u = ms(at - Number(sent));
    const d = ms(Number(recv) - at);
    up.push(u);
    down.push(d);
    // Pérdida del mismo sentido mientras esa etapa estaba en curso o justo antes: puede bloquear el canal ordenado.
    const margin = freq; // 1 s: una pérdida anterior aún sin reenviar retiene lo que viene detrás
    const upLoss = hasDrop('C', Number(sent) - margin, at);
    const downLoss = hasDrop('S', at - margin, Number(recv));
    (upLoss ? upLossy : upClean).push(u);
    (downLoss ? downLossy : downClean).push(d);
    if (all > 500) {
      slow.n++;
      if (upLoss) slow.upLoss++;
      if (downLoss) slow.downLoss++;
      if (!upLoss && !downLoss) slow.noLoss++;
    }
  }
}
console.log(`Carpeta: ${path.basename(dir)}`);
console.log(line('Total orden→resultado', total));
console.log(line('Ida: enviar→aplicar', up));
console.log(line('Vuelta: aplicar→recibir', down));
console.log(line('Ida sin pérdida cercana', upClean));
console.log(line('Ida con pérdida cercana', upLossy));
console.log(line('Vuelta sin pérdida cercana', downClean));
console.log(line('Vuelta con pérdida cercana', downLossy));
console.log(line('Reenvío cliente→servidor', resendGap.C));
console.log(line('Reenvío servidor→cliente', resendGap.S));
console.log(`Fiables: cliente ${droppedReliable.C}/${sentReliable.C} perdidos · servidor ${droppedReliable.S}/${sentReliable.S} perdidos · reenvíos innecesarios: cliente ${spurious.C}, servidor ${spurious.S}`);
console.log(`Órdenes >500 ms: ${slow.n} · con pérdida a la ida ${slow.upLoss} · a la vuelta ${slow.downLoss} · sin pérdida cercana ${slow.noLoss} · sin casar ${unmatched.length}`);
