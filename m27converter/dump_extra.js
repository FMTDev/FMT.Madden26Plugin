const MaddenRosterHelper = require('./helpers/MaddenRosterHelper');

async function dumpTables(filePath, names) {
    const helper = new MaddenRosterHelper();
    const file = await helper.load(filePath);
    for (const t of file.tables) {
        if (!names.includes(t.name)) continue;
        console.log(`\n=== ${t.name} (${filePath.split('\\').pop()}) type=${t.type} entries=${t.numEntries} unknown1=${t.unknown1} unknown2=${t.unknown2}`);
        const sample = t.records.slice(0, 5);
        sample.forEach((r, i) => {
            const keys = Object.keys(r.fields);
            const kv = keys.slice(0, 12).map(k => {
                const f = r.fields[k];
                let v;
                if (f.type === 1) v = `"${Buffer.from(f.raw).toString('ascii')}"`;
                else if (f.type === 4 || f.type === 5) v = `[subtable ${f.value.numEntries}]`;
                else if (f.type === 10) v = `float`;
                else v = f.raw.toString('hex');
                return `${k}:${v}`;
            }).join(' ');
            console.log(`  rec[${i}] idx=${r.index} fields=${keys.length}: ${kv}${keys.length > 12 ? ' ...' : ''}`);
        });
        // field name census
        const fieldCounts = {};
        for (const r of t.records) for (const k of Object.keys(r.fields)) fieldCounts[k] = (fieldCounts[k] || 0) + 1;
        console.log(`  field census (${Object.keys(fieldCounts).length} unique): ${Object.keys(fieldCounts).sort().join(',')}`);
    }
}

async function main() {
    await dumpTables('C:\\Users\\Ninja\\Documents\\Madden NFL 26\\saves\\ROSTER-Official27TEST', ['PLGS', 'DCHT', 'TEAM']);
    await dumpTables('C:\\Users\\Ninja\\Documents\\Madden NFL 27 Beta\\Saves\\ROSTER-MADDEN27', ['INJY', 'PLCT', 'PRSN', 'DCHT', 'TEAM']);
}

main().catch(e => { console.error(e); process.exit(1); });
