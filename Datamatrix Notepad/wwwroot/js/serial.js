let activePort = null;
let activeReader = null;
let readLoopPromise = null;
let dotNetReference = null;
let isClosing = false;

export function isSupported() {
    return "serial" in navigator;
}

export async function requestAndOpen(options, callbackReference) {
    if (!isSupported()) {
        throw new Error("Web Serial API недоступен в этом браузере.");
    }

    await close();

    let selectedPort;
    try {
        // requestPort() must be called from a user activation such as a button click.
        // https://wicg.github.io/serial/#dom-serial-requestport
        selectedPort = await navigator.serial.requestPort();
    } catch (error) {
        if (error?.name === "NotFoundError") {
            return { cancelled: true };
        }

        throw error;
    }

    const info = await openPort(selectedPort, options, callbackReference);
    return {
        cancelled: false,
        usbVendorId: info.usbVendorId,
        usbProductId: info.usbProductId
    };
}

export async function openPreviouslyGranted(
    options,
    expectedVendorId,
    expectedProductId,
    callbackReference) {
    if (!isSupported()) {
        throw new Error("Web Serial API недоступен в этом браузере.");
    }

    await close();

    const permittedPorts = await navigator.serial.getPorts();
    const matchingPorts = permittedPorts.filter(port =>
        matchesSavedIdentifiers(port, expectedVendorId, expectedProductId));

    if (matchingPorts.length === 0) {
        return { status: "notFound" };
    }

    if (matchingPorts.length > 1) {
        return { status: "ambiguous" };
    }

    const info = await openPort(matchingPorts[0], options, callbackReference);
    return {
        status: "connected",
        usbVendorId: info.usbVendorId,
        usbProductId: info.usbProductId
    };
}

export async function close() {
    if (!activePort && !readLoopPromise) {
        return;
    }

    isClosing = true;
    const portToClose = activePort;
    const loopToAwait = readLoopPromise;

    if (activeReader) {
        await activeReader.cancel().catch(() => {
            // Cancellation intentionally interrupts reader.read().
        });
    }

    if (loopToAwait) {
        await loopToAwait.catch(() => {
            // The read loop reports unexpected failures to .NET.
        });
    }

    if (portToClose) {
        await portToClose.close();
    }

    activePort = null;
    activeReader = null;
    readLoopPromise = null;
    dotNetReference = null;
    isClosing = false;
}

async function readFromPort(port, encoding, callbackReference) {
    const decoder = new TextDecoder(encoding);

    try {
        // Serial chunks are arbitrary. The nested-loop pattern handles recoverable
        // stream errors and releases each reader before the port is closed.
        // https://wicg.github.io/serial/#dom-serialport-readable
        while (port === activePort && port.readable && !isClosing) {
            const reader = port.readable.getReader();
            activeReader = reader;

            try {
                while (!isClosing) {
                    const { value, done } = await reader.read();
                    if (done) {
                        break;
                    }

                    if (value?.byteLength > 0) {
                        const text = decoder.decode(value, { stream: true });
                        if (text.length > 0) {
                            await callbackReference.invokeMethodAsync("ReceiveChunkAsync", text);
                        }
                    }
                }
            } catch (error) {
                if (!isClosing) {
                    await callbackReference.invokeMethodAsync(
                        "ReportReadErrorAsync",
                        describeError(error));
                }
            } finally {
                if (activeReader === reader) {
                    activeReader = null;
                }

                reader.releaseLock();
            }
        }

        const trailingText = decoder.decode();
        if (!isClosing && trailingText.length > 0) {
            await callbackReference.invokeMethodAsync("ReceiveChunkAsync", trailingText);
        }
    } finally {
        if (!isClosing && port === activePort) {
            activePort = null;
            activeReader = null;
            readLoopPromise = null;
            dotNetReference = null;
            await callbackReference.invokeMethodAsync("NotifyDisconnectedAsync");
        }
    }
}

async function openPort(port, options, callbackReference) {
    // baudRate is required; the remaining values are explicit approved defaults.
    // https://wicg.github.io/serial/#serialoptions-dictionary
    await port.open({
        baudRate: options.baudRate,
        dataBits: options.dataBits,
        parity: options.parity,
        stopBits: options.stopBits,
        flowControl: options.flowControl
    });

    activePort = port;
    dotNetReference = callbackReference;
    isClosing = false;
    readLoopPromise = readFromPort(port, options.encoding, callbackReference);
    return port.getInfo();
}

function matchesSavedIdentifiers(port, expectedVendorId, expectedProductId) {
    const info = port.getInfo();
    const vendorMatches =
        expectedVendorId === null ||
        expectedVendorId === undefined ||
        info.usbVendorId === expectedVendorId;
    const productMatches =
        expectedProductId === null ||
        expectedProductId === undefined ||
        info.usbProductId === expectedProductId;

    return vendorMatches && productMatches;
}

function describeError(error) {
    if (typeof error?.message === "string" && error.message.length > 0) {
        return error.message;
    }

    return "Неизвестная ошибка чтения последовательного порта.";
}
