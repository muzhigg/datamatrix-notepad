const pendingWrites = new Map();

export function read(key) {
    return localStorage.getItem(key);
}

export function scheduleWrite(key, value, debounceMilliseconds) {
    return new Promise((resolve, reject) => {
        let pending = pendingWrites.get(key);

        if (pending) {
            clearTimeout(pending.timer);
            pending.value = value;
            pending.waiters.push({ resolve, reject });
        } else {
            pending = {
                value,
                timer: null,
                waiters: [{ resolve, reject }]
            };
            pendingWrites.set(key, pending);
        }

        pending.timer = setTimeout(
            () => flushKey(key),
            Math.max(0, debounceMilliseconds));
    });
}

export function writeNow(key, value) {
    const pending = pendingWrites.get(key);
    if (pending) {
        clearTimeout(pending.timer);
        pending.value = value;
        flushKey(key);
        return;
    }

    localStorage.setItem(key, value);
}

export function dispose() {
    flushAll();
    globalThis.removeEventListener("pagehide", flushAll);
}

function flushAll() {
    for (const key of [...pendingWrites.keys()]) {
        flushKey(key);
    }
}

function flushKey(key) {
    const pending = pendingWrites.get(key);
    if (!pending) {
        return;
    }

    clearTimeout(pending.timer);
    pendingWrites.delete(key);

    try {
        localStorage.setItem(key, pending.value);
        for (const waiter of pending.waiters) {
            waiter.resolve();
        }
    } catch (error) {
        for (const waiter of pending.waiters) {
            waiter.reject(error);
        }
    }
}

globalThis.addEventListener("pagehide", flushAll);
