import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const collabDbName = "ProCad.Collaboration";
const legacyCollabDbName = ["A", "Cad", "Inspector.Collaboration"].join("");
const collabStoreName = "cad-collab-kv";
const collabKeyPrefix = "procad.collab";
const legacyCollabKeyPrefix = ["acad", "inspector.collab"].join("");

function toLegacyCollabKey(key) {
    if (typeof key === "string" && key.startsWith(`${collabKeyPrefix}.`)) {
        return `${legacyCollabKeyPrefix}${key.slice(collabKeyPrefix.length)}`;
    }

    return key;
}

function isLegacyCollabKey(key) {
    return typeof key === "string" && key.startsWith(`${legacyCollabKeyPrefix}.`);
}

async function collabDbExists(dbName) {
    if (typeof globalThis.indexedDB?.databases !== "function") {
        return true;
    }

    try {
        const databases = await globalThis.indexedDB.databases();
        return databases.some((database) => database.name === dbName);
    } catch {
        return true;
    }
}

function openCollabDb(dbName, createIfMissing = true) {
    return new Promise((resolve, reject) => {
        if (!globalThis.indexedDB) {
            resolve(null);
            return;
        }

        const open = async () => {
            if (!createIfMissing && !(await collabDbExists(dbName))) {
                resolve(null);
                return;
            }

            const request = globalThis.indexedDB.open(dbName, 1);
            request.onupgradeneeded = () => {
                const db = request.result;
                if (!db.objectStoreNames.contains(collabStoreName)) {
                    db.createObjectStore(collabStoreName);
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error ?? new Error("IndexedDB open failed."));
        };

        open().catch(reject);
    });
}

async function runCollabRequest(dbName, mode, createRequest, createIfMissing = true) {
    const db = await openCollabDb(dbName, createIfMissing);
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

async function tryGetCollabValue(dbName, key, createIfMissing = true) {
    const result = await runCollabRequest(dbName, "readonly", (store) => store.get(key), createIfMissing);
    return typeof result.value === "string" ? result.value : null;
}

globalThis.proCadCollab = globalThis.proCadCollab ?? {};
globalThis.proCadCollab.idbAvailable = () => !!globalThis.indexedDB;
globalThis.proCadCollab.idbGet = async (key) => {
    try {
        const currentValue = await tryGetCollabValue(collabDbName, key);
        if (typeof currentValue === "string") {
            return currentValue;
        }

        return await tryGetCollabValue(legacyCollabDbName, toLegacyCollabKey(key), false);
    } catch {
        return null;
    }
};

globalThis.proCadCollab.idbSet = async (key, value) => {
    try {
        const result = await runCollabRequest(collabDbName, "readwrite", (store) => store.put(value, key));
        return !!result.supported;
    } catch {
        return false;
    }
};

globalThis.proCadCollab.idbRemove = async (key) => {
    try {
        const result = await runCollabRequest(collabDbName, "readwrite", (store) => store.delete(key));
        const legacyKey = toLegacyCollabKey(key);
        if (legacyKey !== key || isLegacyCollabKey(key)) {
            await runCollabRequest(legacyCollabDbName, "readwrite", (store) => store.delete(legacyKey), false);
        }

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
