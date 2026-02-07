import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const collabDbName = "ACadInspector.Collaboration";
const collabStoreName = "cad-collab-kv";

function openCollabDb() {
    return new Promise((resolve, reject) => {
        if (!globalThis.indexedDB) {
            resolve(null);
            return;
        }

        const request = globalThis.indexedDB.open(collabDbName, 1);
        request.onupgradeneeded = () => {
            const db = request.result;
            if (!db.objectStoreNames.contains(collabStoreName)) {
                db.createObjectStore(collabStoreName);
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error ?? new Error("IndexedDB open failed."));
    });
}

async function runCollabRequest(mode, createRequest) {
    const db = await openCollabDb();
    if (!db) {
        return { supported: false, value: null };
    }

    try {
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(collabStoreName, mode);
            const store = tx.objectStore(collabStoreName);

            let request;
            try {
                request = createRequest(store);
            } catch (error) {
                reject(error);
                return;
            }

            request.onsuccess = () => resolve({ supported: true, value: request.result ?? null });
            request.onerror = () => reject(request.error ?? new Error("IndexedDB request failed."));
            tx.onabort = () => reject(tx.error ?? new Error("IndexedDB transaction aborted."));
            tx.onerror = () => reject(tx.error ?? new Error("IndexedDB transaction failed."));
        });
    } finally {
        db.close();
    }
}

globalThis.acadInspectorCollab = globalThis.acadInspectorCollab ?? {};
globalThis.acadInspectorCollab.idbAvailable = () => !!globalThis.indexedDB;
globalThis.acadInspectorCollab.idbGet = async (key) => {
    try {
        const result = await runCollabRequest("readonly", (store) => store.get(key));
        if (!result.supported || typeof result.value !== "string") {
            return null;
        }

        return result.value;
    } catch {
        return null;
    }
};

globalThis.acadInspectorCollab.idbSet = async (key, value) => {
    try {
        const result = await runCollabRequest("readwrite", (store) => store.put(value, key));
        return !!result.supported;
    } catch {
        return false;
    }
};

globalThis.acadInspectorCollab.idbRemove = async (key) => {
    try {
        const result = await runCollabRequest("readwrite", (store) => store.delete(key));
        return !!result.supported;
    } catch {
        return false;
    }
};

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
