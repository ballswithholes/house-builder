'use strict';
// Splices the GENERATED blocks of gen-md.js's output into ITEMS.src.md's <!-- INSERT:X --> tags → ITEMS.md.
// Usage: node design-v4/items/splice-md.js <tables.md> [src = design-v4/items/ITEMS.src.md] [out = design-v4/ITEMS.md]
const fs = require('fs'), path = require('path');
const tables = fs.readFileSync(process.argv[2], 'utf8');
const src = fs.readFileSync(process.argv[3] || path.join(__dirname, 'ITEMS.src.md'), 'utf8');
const out = process.argv[4] || path.join(__dirname, '..', 'ITEMS.md');
const blocks = {};
for (const m of tables.matchAll(/<!-- GENERATED:(\w+) -->[\s\S]*?<!-- \/GENERATED:\1 -->/g)) blocks[m[1]] = m[0];
let missing = [];
const res = src.replace(/<!-- INSERT:(\w+) -->/g, (all, k) => { if (!blocks[k]) { missing.push(k); return all; } return blocks[k].replace('<!-- GENERATED:' + k + ' -->', '<!-- GENERATED:' + k + ' (gen-md.js; do not edit by hand) -->'); });
if (missing.length) { console.error('missing blocks: ' + missing.join(', ')); process.exit(1); }
fs.writeFileSync(out, res);
console.log('ITEMS.md written: ' + Object.keys(blocks).length + ' blocks, ' + res.split('\n').length + ' lines');
