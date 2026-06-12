// ScadBundler Live — minimal JS interop (Design §4).
//
// The browser has no managed surface for: drag-drop file/folder reading (the entries API recovers
// relative paths), the webkitdirectory folder picker, clipboard writes, and Blob downloads. Everything
// else (parse / analyze / bundle / emit) runs in C#. There is NO JS library here — only DOM APIs. Files
// are read to text (or, for a .zip, Base64) and handed to .NET, which unzips with the BCL ZipArchive.

window.scadLive = {
    dotNetRef: null,

    // Store the DropZone's .NET object reference so collected files can be pushed back to managed code.
    init: function (dotNetRef) {
        this.dotNetRef = dotNetRef;
    },

    // Attach drag-drop handlers to the drop zone element.
    registerDropZone: function (element) {
        const self = this;

        element.addEventListener('dragover', function (e) {
            e.preventDefault();
            element.classList.add('dragover');
        });
        element.addEventListener('dragleave', function () {
            element.classList.remove('dragover');
        });
        element.addEventListener('drop', async function (e) {
            e.preventDefault();
            element.classList.remove('dragover');

            const collected = [];
            const items = e.dataTransfer.items;

            // webkitGetAsEntry must be called synchronously on the items before any await, so snapshot first.
            const entries = [];
            const looseFiles = [];
            if (items) {
                for (let i = 0; i < items.length; i++) {
                    const entry = items[i].webkitGetAsEntry ? items[i].webkitGetAsEntry() : null;
                    if (entry) {
                        entries.push(entry);
                    } else {
                        const file = items[i].getAsFile && items[i].getAsFile();
                        if (file) { looseFiles.push(file); }
                    }
                }
            }

            for (const entry of entries) {
                await self.walkEntry(entry, collected);
            }
            for (const file of looseFiles) {
                await self.readFile(file, file.name, collected);
            }
            // Fallback: a browser that exposed no items but did expose files (rare).
            if (!entries.length && !looseFiles.length && e.dataTransfer.files) {
                for (const file of e.dataTransfer.files) {
                    await self.readFile(file, file.name, collected);
                }
            }

            await self.dispatch(collected);
        });
    },

    // Recursively read a FileSystemEntry, preserving its relative path for folder structure.
    walkEntry: async function (entry, out) {
        if (entry.isFile) {
            const file = await new Promise(function (resolve, reject) { entry.file(resolve, reject); });
            const name = entry.fullPath ? entry.fullPath.replace(/^\//, '') : file.name;
            await this.readFile(file, name, out);
        } else if (entry.isDirectory) {
            const reader = entry.createReader();
            // readEntries yields in batches; loop until it returns an empty batch.
            while (true) {
                const batch = await new Promise(function (resolve, reject) { reader.readEntries(resolve, reject); });
                if (!batch.length) { break; }
                for (const child of batch) {
                    await this.walkEntry(child, out);
                }
            }
        }
    },

    // Read one File: .scad as text, .zip as Base64; everything else is ignored.
    readFile: async function (file, name, out) {
        const lower = name.toLowerCase();
        if (lower.endsWith('.zip')) {
            const buffer = await file.arrayBuffer();
            out.push({ name: name, kind: 'zip', content: this.toBase64(buffer) });
        } else if (lower.endsWith('.scad')) {
            const text = await file.text();
            out.push({ name: name, kind: 'text', content: text });
        }
    },

    toBase64: function (arrayBuffer) {
        const bytes = new Uint8Array(arrayBuffer);
        let binary = '';
        const chunk = 0x8000;
        for (let i = 0; i < bytes.length; i += chunk) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
        }
        return btoa(binary);
    },

    // "Choose files": a plain multi-file picker (loose names).
    pickFiles: function () {
        this.openPicker(false);
    },

    // "Choose folder": a webkitdirectory picker (each file carries webkitRelativePath).
    pickFolder: function () {
        this.openPicker(true);
    },

    openPicker: function (directory) {
        const self = this;
        const input = document.createElement('input');
        input.type = 'file';
        input.multiple = true;
        if (directory) {
            input.webkitdirectory = true;
        } else {
            input.accept = '.scad,.zip';
        }

        input.addEventListener('change', async function () {
            const out = [];
            for (const file of input.files) {
                const rel = file.webkitRelativePath && file.webkitRelativePath.length
                    ? file.webkitRelativePath
                    : file.name;
                await self.readFile(file, rel, out);
            }
            await self.dispatch(out);
        });

        input.click();
    },

    dispatch: async function (items) {
        if (this.dotNetRef && items.length) {
            await this.dotNetRef.invokeMethodAsync('Ingest', items);
        }
    },

    copyText: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (e) {
            return false;
        }
    },

    download: function (filename, text) {
        const blob = new Blob([text], { type: 'application/octet-stream' });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = filename;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    }
};
