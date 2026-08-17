const path = require('path');
const fs = require('fs');
const MaddenRosterHelper = require('./helpers/MaddenRosterHelper');

const M26 = 'C:\\Users\\Ninja\\Documents\\Madden NFL 26\\saves\\ROSTER-Official27TEST';
const M27 = 'C:\\Users\\Ninja\\Documents\\Madden NFL 27 Beta\\Saves\\ROSTER-MADDEN27';
const OUT = 'C:\\Users\\Ninja\\AppData\\Local\\Temp\\opencode\\rtout\\ROSTER-M27CONVERTED';
const SIZE = 6291530;

async function main() {
    const h26 = new MaddenRosterHelper();
    const m26 = await h26.load(M26);

    const h27 = new MaddenRosterHelper();
    const m27 = await h27.load(M27);

    console.log('M26 tables:', m26.tables.map(t => t.name).join(','));
    console.log('M27 tables:', m27.tables.map(t => t.name).join(','));

    // --- salary migration: M27 stores salaries in PLCT; M26 expects PSA*/PSB* in PLAY ---
    const plct = m27.tables.find(t => t.name === 'PLCT');
    const salaryByPgid = new Map();
    for (const r of plct.records) {
        const pgid = r.fields.PGID ? r.fields.PGID.value : null;
        salaryByPgid.set(pgid, r);
    }
    const salaryFields = ['PSA0','PSA1','PSA2','PSA3','PSA4','PSA5','PSA6','PSB0','PSB1','PSB2','PSB3','PSB4','PCON'];
    const play = m27.tables.find(t => t.name === 'PLAY');
    let salMigrated = 0;
    for (const r of play.records) {
        const pgid = r.fields.PGID ? r.fields.PGID.value : null;
        const s = salaryByPgid.get(pgid);
        if (!s) continue;
        for (const k of salaryFields) {
            if (r.fields[k] && s.fields[k]) {
                r.fields[k].value = s.fields[k].value;
                r.fields[k].raw = s.fields[k].raw;
                salMigrated++;
            }
        }
    }
    console.log('salary fields migrated:', salMigrated);

    // --- build new table set: drop M27-only, add M26-only PLGS ---
    const drop = new Set(['INJY', 'PLCT', 'PRSN']);
    const newTables = m27.tables.filter(t => !drop.has(t.name));
    const plgs = m26.tables.find(t => t.name === 'PLGS');
    newTables.push(plgs);

    // --- assign into M26 container (keeps M26 header/version) ---
    m26.tables = newTables;

    await h26.save(OUT);

    // normalize to exact save size (pad or truncate) - matches ROSTER-CHKSUMTEST approach
    const fb = fs.readFileSync(OUT);
    if (fb.length > SIZE) fs.writeFileSync(OUT, fb.subarray(0, SIZE));
    else if (fb.length < SIZE) fs.writeFileSync(OUT, Buffer.concat([fb, Buffer.alloc(SIZE - fb.length)]));

    console.log('wrote:', OUT, 'size:', fs.statSync(OUT).size);

    // --- verify: reload output and print table list + CRC ---
    const hv = new MaddenRosterHelper();
    const v = await hv.load(OUT);
    console.log('reload tables:', v.tables.map(t => `${t.name}(type=${t.type},entries=${t.numEntries})`).join(', '));
    const inner = require('zlib').inflateSync(fs.readFileSync(OUT).subarray(0x4A));
    const fb2 = fs.readFileSync(OUT);
    console.log('inner len:', inner.length, 'stored@0x12:', fb2.readUInt32LE(0x12), 'crc@0x1A: 0x' + fb2.readUInt32LE(0x1A).toString(16).padStart(8, '0'));
}

main().catch(e => { console.error(e); process.exit(1); });
