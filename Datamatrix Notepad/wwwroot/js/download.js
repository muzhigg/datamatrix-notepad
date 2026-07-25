export function downloadText(fileName, contentType, content) {
    const blob = new Blob([content], { type: contentType });
    const objectUrl = URL.createObjectURL(blob);
    const link = document.createElement("a");

    try {
        link.href = objectUrl;
        link.download = fileName;
        link.hidden = true;
        document.body.appendChild(link);
        link.click();
    } finally {
        link.remove();
        URL.revokeObjectURL(objectUrl);
    }
}
