const path = require('path');
const zlib = require('zlib');
const fs = require('fs');

const MaddenRosterHelper = require('./helpers/MaddenRosterHelper');

async function roundTrip(src, dst) {
    const helper = new MaddenRosterHelper();
    const file = await helper.load(src);
    console.log('tables:', file.tables.map(t => `${t.name}(type=${t.type}, entries=${t.numEntries})`).join(', '));
    await helper.save(dst);
    console.log('saved:', dst);

    // verify: decompress + crc check like game would
    const fb = fs.readFileSync(dst);
    const inner = zlib.inflateSync(fb.subarray(0x4A));
    const expectedCrc = fb.readUInt32LE(0x1A);
    const expectedLen = fb.readUInt32LE(0x12);

    // CRC-32 BE (poly 0x04C11DB7)
    const CRCPOLY_BE = 0x04c11db7;
    let crc = 0x80000000, crcTableBe = [0];
    for (let i = 1; i < 1 << 4; i <<= 1) {
        crc = (crc << 1) ^ (((crc & 0x80000000) != 0) ? CRCPOLY_BE : 0);
        for (let j = 0; j < i; j++) crcTableBe[i + j] = crc ^ crcTableBe[j];
    }
    let crcState = (0 ^ 0xFFFFFFFF) >>> 0;
    for (let x = 0; x < inner.length; x++) {
        crcState = (crcState ^ (inner[x] << 24)) >>> 0;
        crcState = ((crcState << 4) ^ crcTableBe[crcState >>> 28]) >>> 0;
        crcState = ((crcState << 4) ^ crcTableBe[crcState >>> 28]) >>> 0;
    }
    const calcCrc = (crcState ^ 0xFFFFFFFF) >>> 0;

    console.log(`len: stored=${expectedLen} actual=${inner.length} match=${expectedLen === inner.length}`);
    console.log(`crc: stored=0x${expectedCrc.toString(16).padStart(8, '0')} actual=0x${calcCrc.toString(16).padStart(8, '0')} match=${expectedCrc === calcCrc}`);
}

async function main() {
    const outDir = 'C:\\Users\\Ninja\\AppData\\Local\\Temp\\opencode\\rtout';
    fs.mkdirSync(outDir, { recursive: true });
    await roundTrip(
        'C:\\Users\\Ninja\\Documents\\Madden NFL 26\\saves\\ROSTER-Official27TEST',
        path.join(outDir, 'ROSTER-RT26')
    );
    await roundTrip(
        'C:\\Users\\Ninja\\Documents\\Madden NFL 27 Beta\\Saves\\ROSTER-MADDEN27',
        path.join(outDir, 'ROSTER-RT27')
    );
}

main().catch(e => { console.error(e); process.exit(1); });
