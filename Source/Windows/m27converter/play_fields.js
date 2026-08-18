const MaddenRosterHelper = require('./helpers/MaddenRosterHelper');

async function playFields(filePath) {
    const helper = new MaddenRosterHelper();
    const file = await helper.load(filePath);
    const t = file.tables.find(x => x.name === 'PLAY');
    const set = {};
    for (const r of t.records) for (const k of Object.keys(r.fields)) set[k] = 1;
    return { name: filePath.split('\\').pop(), fields: Object.keys(set).sort(), table: t };
}

async function main() {
    const m26 = await playFields('C:\\Users\\Ninja\\Documents\\Madden NFL 26\\saves\\ROSTER-Official27TEST');
    const m27 = await playFields('C:\\Users\\Ninja\\Documents\\Madden NFL 27 Beta\\Saves\\ROSTER-MADDEN27');
    const s26 = new Set(m26.fields), s27 = new Set(m27.fields);
    const m26only = m27.fields.filter(f => !s26.has(f));
    const m26missing = m26.fields.filter(f => !s27.has(f));
    console.log(`PLAY fields: M26=${m26.fields.length} M27=${m27.fields.length}`);
    console.log('M27-only (to strip):', m26only.join(','));
    console.log('M26 missing from M27 (must supply!):', m26missing.join(','));
    // salary field check
    const sal = m26.fields.filter(f => f.startsWith('PSA') || f.startsWith('PSB'));
    console.log('M26 salary fields in PLAY:', sal.join(','));

    // check PLCT's salary fields available
    const p27 = m27.table; // PLAY table of M27
    const plct = (await (new MaddenRosterHelper().load('C:\\Users\\Ninja\\Documents\\Madden NFL 27 Beta\\Saves\\ROSTER-MADDEN27'))).tables.find(x => x.name === 'PLCT');
    console.log('PLCT salary fields:', Object.keys(plct.records[0].fields).filter(f => f.startsWith('PSA') || f.startsWith('PSB') || f === 'PCON').join(','));
}

main().catch(e => { console.error(e); process.exit(1); });
