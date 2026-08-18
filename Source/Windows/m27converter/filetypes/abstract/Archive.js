const File = require('./File');
const ArchivedFile = require('./ArchivedFile');

class Archive extends File {
    constructor (stream) {
        super(stream);
        this._archivedFiles = [];
    };

    get archivedFiles () {
        return this._archivedFiles;
    };

    compress() {
        throw new Error('Method not implemented.');
    };

    _addArchivedFile(compressedData, metadata) {
        this._archivedFiles.push(new ArchivedFile(compressedData, metadata));
    };
};

module.exports = Archive;