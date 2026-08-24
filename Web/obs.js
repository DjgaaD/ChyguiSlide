// OBS Browser Source — только текст, прозрачный фон. Обновления по WebSocket.
// Текст приходит одной строкой; перенос и кегль подстраиваются под размер источника в OBS.
let currentLayer = 'a';
let transitionStyle = 'FadeSlide';
let transitionDurationMs = 750;
let _busy = false;
let _pending = null;

const themeState = {
    primaryColor: '#f5f2ea',
    maxFontSize: 120,
    fontFamily: 'Segoe UI, sans-serif',
    fontWeight: 400,
    textAlignment: 'center',
    lineSpacing: 12,
    textOutlineColor: '#000000',
    textOutlineThickness: 0,
    textOutlineOpacity: 1,
    showBibleReference: true,
    bibleReferencePlacement: 'Above',
    bibleReferenceAlignment: 'Center',
    referenceFontSize: 28
};

const layerA = document.getElementById('layer-a');
const layerB = document.getElementById('layer-b');
const stage = document.getElementById('stage');
const backdropEl = document.getElementById('backdrop');

const backdropConfig = {
    enabled: false,
    opacity: 0.9
};

if (layerA && layerB) {
    layerA.style.opacity = '1';
    layerB.style.opacity = '0';
}

applyVirtualStageSize();
loadBackdropFromUrl();

function loadBackdropFromUrl() {
    const params = new URLSearchParams(location.search);
    const raw = params.get('backdrop');
    if (raw == null || raw === '') {
        return;
    }

    const value = parseFloat(raw);
    if (Number.isNaN(value)) {
        return;
    }

    backdropConfig.enabled = value > 0;
    backdropConfig.opacity = value > 1 ? Math.min(1, value / 100) : value;
}

function applyVirtualStageSize() {
    if (!stage) {
        return;
    }

    const params = new URLSearchParams(location.search);
    const w = parseInt(params.get('w') || '', 10);
    const h = parseInt(params.get('h') || '', 10);
    if (w > 0 && h > 0) {
        stage.style.width = `${w}px`;
        stage.style.height = `${h}px`;
        stage.style.position = 'relative';
        stage.style.margin = '0 auto';
    }
}

function visibleLayer() {
    return currentLayer === 'a' ? layerA : layerB;
}

function hiddenLayer() {
    return currentLayer === 'a' ? layerB : layerA;
}

function slideTextFrom(data) {
    if (typeof data.text === 'string') {
        return data.text.trim();
    }

    return (data.lines || [])
        .map(line => String(line || '').trim())
        .filter(Boolean)
        .join(' ');
}

function handleMessage(data) {
    switch (data.type) {
        case 'updateSlide':
            updateSlide(data);
            break;
        case 'updateTheme':
            mergeThemeFrom(data);
            if (!_busy) {
                applyThemeToLayer(visibleLayer(), { applyLayout: false });
                refitLayerText(visibleLayer());
            }
            break;
        case 'updateBackdrop':
            updateBackdrop(data);
            break;
        case 'setTransitionStyle':
            transitionStyle = data.style || transitionStyle;
            break;
        case 'setTransitionDuration':
            transitionDurationMs = data.durationMs || transitionDurationMs;
            break;
    }
}

function mergeBackdropConfig(data) {
    if (data.backdropEnabled != null) {
        backdropConfig.enabled = !!data.backdropEnabled;
    } else if (data.enabled != null) {
        backdropConfig.enabled = !!data.enabled;
    }

    if (typeof data.backdropOpacity === 'number') {
        backdropConfig.opacity = data.backdropOpacity;
    } else if (typeof data.opacity === 'number') {
        backdropConfig.opacity = data.opacity;
    }
}

function hasVisibleSlideText() {
    const el = visibleLayer()?.querySelector('.slide-text');
    return !!(el && el.textContent && el.textContent.trim());
}

function updateBackdrop(data) {
    mergeBackdropConfig(data);
    applyBackdropForSlide(hasVisibleSlideText());
}

function applyBackdropForSlide(hasText) {
    const show = hasText && backdropConfig.enabled && backdropConfig.opacity > 0;
    const color = show
        ? `rgba(0, 0, 0, ${Math.max(0, Math.min(1, backdropConfig.opacity))})`
        : 'transparent';

    document.body.style.backgroundColor = color;
    document.documentElement.style.backgroundColor = 'transparent';
    if (backdropEl) {
        backdropEl.style.backgroundColor = color;
    }
}

function normalizeTextAlignment(value) {
    const a = String(value ?? 'center').toLowerCase();
    return a === 'left' || a === 'right' || a === 'justify' ? a : 'center';
}

function mergeThemeFrom(data) {
    if (data.primaryColor) themeState.primaryColor = data.primaryColor;
    if (data.maxFontSize) themeState.maxFontSize = data.maxFontSize;
    else if (data.fontSize) themeState.maxFontSize = data.fontSize;
    if (data.fontFamily) themeState.fontFamily = data.fontFamily;
    if (data.fontWeight) themeState.fontWeight = data.fontWeight;
    if (data.textAlignment) themeState.textAlignment = normalizeTextAlignment(data.textAlignment);
    if (data.lineSpacing) themeState.lineSpacing = data.lineSpacing;
    if (data.textOutlineColor) themeState.textOutlineColor = data.textOutlineColor;
    if (data.textOutlineThickness != null) themeState.textOutlineThickness = data.textOutlineThickness;
    if (data.textOutlineOpacity != null) themeState.textOutlineOpacity = data.textOutlineOpacity;
    if (data.showBibleReference != null) themeState.showBibleReference = data.showBibleReference;
    if (data.bibleReferencePlacement) themeState.bibleReferencePlacement = data.bibleReferencePlacement;
    if (data.bibleReferenceAlignment) themeState.bibleReferenceAlignment = data.bibleReferenceAlignment;
    if (data.referenceFontSize) themeState.referenceFontSize = data.referenceFontSize;
}

async function updateSlide(data) {
    mergeThemeFrom(data);
    mergeBackdropConfig(data);
    if (data.transitionStyle) {
        transitionStyle = data.transitionStyle;
    }

    if (_busy) {
        _pending = data;
        return;
    }

    _busy = true;
    try {
        const text = slideTextFrom(data);
        await playSlide(data);
        applyBackdropForSlide(!!text);
    } finally {
        _busy = false;
        if (_pending) {
            const next = _pending;
            _pending = null;
            await updateSlide(next);
        }
    }
}

async function playSlide(data) {
    const style = transitionStyle || 'FadeSlide';
    const text = slideTextFrom(data);
    const caption = data.referenceCaption || null;
    const dur = transitionDurationMs > 0 ? transitionDurationMs : 750;

    if (!text) {
        if (layerA) {
            layerA.innerHTML = '';
            layerA.style.opacity = '1';
        }
        if (layerB) {
            layerB.innerHTML = '';
            layerB.style.opacity = '0';
        }
        currentLayer = 'a';
        return;
    }

    switch (style) {
        case 'Fade':
        case 'Crossfade':
            await runCrossfade(text, caption, Math.min(dur, 500));
            break;
        case 'BlurSharp':
            await runBlur(text, caption, dur);
            break;
        case 'Stagger':
            await runCrossfade(text, caption, dur);
            break;
        default:
            await runFadeSlide(text, caption, dur);
            break;
    }
}

function applyThemeToLayer(el, { applyLayout = true } = {}) {
    el.style.color = themeState.primaryColor;
    const outline = buildTextOutlineStyle(
        themeState.textOutlineThickness,
        themeState.textOutlineColor,
        themeState.textOutlineOpacity ?? 1
    );
    el.style.webkitTextStroke = outline.webkitTextStroke;
    el.style.paintOrder = outline.paintOrder;
    el.style.textShadow = outline.textShadow;

    if (!applyLayout) {
        return;
    }

    el.style.fontFamily = themeState.fontFamily;
    el.style.fontWeight = String(themeState.fontWeight);
    el.style.textAlign = themeState.textAlignment;
    el.style.alignItems =
        themeState.textAlignment === 'left' ? 'flex-start'
            : themeState.textAlignment === 'right' ? 'flex-end'
                : themeState.textAlignment === 'justify' ? 'stretch'
                    : 'center';
    el.style.gap = `${themeState.lineSpacing}px`;
}

function fillLayer(el, text, referenceCaption) {
    applyThemeToLayer(el, { applyLayout: true });

    const showRef = themeState.showBibleReference && referenceCaption;
    const placement = themeState.bibleReferencePlacement || 'Above';
    const alignClass =
        themeState.bibleReferenceAlignment === 'Left' ? 'align-left'
            : themeState.bibleReferenceAlignment === 'Right' ? 'align-right'
                : 'align-center';

    el.classList.remove('has-reference', 'pos-screen-ref', 'pos-screen-ref-bottom');

    let refHtml = '';
    if (showRef) {
        let posClass = '';
        if (placement === 'TopOfScreen') {
            posClass = ' pos-top';
            el.classList.add('has-reference', 'pos-screen-ref');
        } else if (placement === 'BottomOfScreen') {
            posClass = ' pos-bottom';
            el.classList.add('has-reference', 'pos-screen-ref-bottom');
        } else {
            el.classList.add('has-reference');
        }

        refHtml = `<div class="reference ${alignClass}${posClass}" style="font-size:${themeState.referenceFontSize}px;">${escapeHtml(referenceCaption)}</div>`;
    }

    const bodyHtml = `<div class="slide-text">${escapeHtml(text || '')}</div>`;

    if (!showRef) {
        el.innerHTML = bodyHtml;
    } else if (placement === 'TopOfScreen' || placement === 'BottomOfScreen') {
        el.innerHTML = refHtml + bodyHtml;
    } else if (placement === 'Below') {
        el.innerHTML = bodyHtml + refHtml;
    } else {
        el.innerHTML = refHtml + bodyHtml;
    }

    refitLayerText(el);
}

function refitLayerText(layerEl) {
    const textEl = layerEl?.querySelector('.slide-text');
    if (!textEl) {
        return;
    }

    textEl.style.textAlign = themeState.textAlignment;
    fitTextToLayer(textEl, layerEl);
}

function fitTextToLayer(textEl, layerEl) {
    const minSize = 10;
    const maxSize = Math.max(minSize, themeState.maxFontSize || 120);
    const pad = 4;

    const layerRect = layerEl.getBoundingClientRect();
    let availW = Math.max(40, layerRect.width - pad * 2);
    let availH = Math.max(24, layerRect.height - pad * 2);

    const ref = layerEl.querySelector('.reference');
    if (ref) {
        const refRect = ref.getBoundingClientRect();
        const isScreenRef = ref.classList.contains('pos-top') || ref.classList.contains('pos-bottom');
        if (!isScreenRef) {
            availH -= refRect.height + (themeState.lineSpacing || 12);
        }
    }

    textEl.style.width = `${availW}px`;
    textEl.style.maxWidth = `${availW}px`;

    let lo = minSize;
    let hi = maxSize;
    let best = minSize;

    while (lo <= hi) {
        const mid = Math.floor((lo + hi) / 2);
        textEl.style.fontSize = `${mid}px`;
        const fits = textEl.scrollHeight <= availH && textEl.scrollWidth <= availW;
        if (fits) {
            best = mid;
            lo = mid + 1;
        } else {
            hi = mid - 1;
        }
    }

    textEl.style.fontSize = `${best}px`;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function buildTextOutlineStyle(thickness, color, opacity) {
    const depth = '0 2px 14px rgba(0,0,0,0.45), 0 1px 2px rgba(0,0,0,0.55)';
    if (!(thickness > 0)) {
        return { webkitTextStroke: '0 transparent', paintOrder: 'normal', textShadow: depth };
    }

    const t = Math.max(0.5, thickness);
    const strokeColor = rgba(color, opacity);
    const parts = [];
    const steps = 16;
    for (let i = 0; i < steps; i++) {
        const a = (i / steps) * Math.PI * 2;
        parts.push(`${(Math.cos(a) * t).toFixed(2)}px ${(Math.sin(a) * t).toFixed(2)}px 0 ${strokeColor}`);
    }
    parts.push(depth);
    return {
        webkitTextStroke: `${Math.max(0.4, t * 0.45).toFixed(2)}px ${strokeColor}`,
        paintOrder: 'stroke fill',
        textShadow: parts.join(', ')
    };
}

function parseColorToRgb(color) {
    const s = String(color || '').trim();
    const hex = s.match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i);
    if (hex) {
        let h = hex[1];
        if (h.length === 3) h = h.split('').map(c => c + c).join('');
        return { r: parseInt(h.slice(0, 2), 16), g: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16) };
    }
    const rgb = s.match(/^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i);
    if (rgb) return { r: +rgb[1], g: +rgb[2], b: +rgb[3] };
    return { r: 245, g: 242, b: 234 };
}

function rgba(color, alpha) {
    const { r, g, b } = parseColorToRgb(color);
    return `rgba(${r},${g},${b},${Math.max(0, Math.min(1, alpha))})`;
}

async function runCrossfade(text, caption, dur) {
    const top = visibleLayer();
    const bottom = hiddenLayer();

    if (!top.querySelector('.slide-text') && !top.querySelector('.reference')) {
        fillLayer(top, text, caption);
        top.style.opacity = '1';
        bottom.style.opacity = '0';
        return;
    }

    fillLayer(bottom, text, caption);
    bottom.style.transition = 'none';
    bottom.style.opacity = '0';
    top.style.transition = 'none';
    void bottom.offsetHeight;

    const easing = `${dur}ms cubic-bezier(0.4,0,0.2,1)`;
    top.style.transition = `opacity ${easing}`;
    bottom.style.transition = `opacity ${easing}`;
    top.style.opacity = '0';
    bottom.style.opacity = '1';
    await sleep(dur);
    currentLayer = currentLayer === 'a' ? 'b' : 'a';
}

async function runFadeSlide(text, caption, dur) {
    const top = visibleLayer();
    const bottom = hiddenLayer();

    if (!top.querySelector('.slide-text')) {
        fillLayer(top, text, caption);
        top.style.opacity = '1';
        top.style.transform = 'translateY(0px)';
        bottom.style.opacity = '0';
        return;
    }

    const stageH = stage?.clientHeight || 280;
    const k = stageH / 280;
    const yIn = 18 * k;
    const yOut = 14 * k;

    fillLayer(bottom, text, caption);
    top.style.transition = 'none';
    top.style.transform = 'translateY(0px)';
    bottom.style.transition = 'none';
    bottom.style.opacity = '0';
    bottom.style.transform = `translateY(${yIn}px)`;
    void bottom.offsetHeight;

    const easing = `${dur}ms cubic-bezier(0.22,0.61,0.36,1)`;
    top.style.transition = `opacity ${easing}, transform ${easing}`;
    bottom.style.transition = `opacity ${easing}, transform ${easing}`;
    top.style.opacity = '0';
    top.style.transform = `translateY(${-yOut}px)`;
    bottom.style.opacity = '1';
    bottom.style.transform = 'translateY(0px)';
    await sleep(dur);
    top.style.transform = 'translateY(0px)';
    currentLayer = currentLayer === 'a' ? 'b' : 'a';
}

async function runBlur(text, caption, dur) {
    const top = visibleLayer();
    const bottom = hiddenLayer();

    if (!top.querySelector('.slide-text')) {
        fillLayer(top, text, caption);
        top.style.opacity = '1';
        top.style.filter = 'blur(0px)';
        bottom.style.opacity = '0';
        return;
    }

    const k = (stage?.clientHeight || 280) / 280;
    fillLayer(bottom, text, caption);
    bottom.style.transition = 'none';
    bottom.style.opacity = '0';
    bottom.style.filter = `blur(${10 * k}px)`;
    top.style.transition = 'none';
    top.style.filter = 'blur(0px)';
    void bottom.offsetHeight;

    const easing = `${dur}ms ease-out`;
    top.style.transition = `opacity ${easing}, filter ${easing}`;
    bottom.style.transition = `opacity ${easing}, filter ${easing}`;
    top.style.opacity = '0';
    top.style.filter = `blur(${6 * k}px)`;
    bottom.style.opacity = '1';
    bottom.style.filter = 'blur(0px)';
    await sleep(dur);
    top.style.filter = 'blur(0px)';
    currentLayer = currentLayer === 'a' ? 'b' : 'a';
}

function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

async function loadInitialState() {
    try {
        const response = await fetch('/api/state');
        if (!response.ok) return;
        const payload = await response.json();
        const messages = payload.messages || [];
        for (const raw of messages) {
            try {
                handleMessage(typeof raw === 'string' ? JSON.parse(raw) : raw);
            } catch {
                // ignore bad message
            }
        }
    } catch {
        // server not ready yet
    } finally {
        applyBackdropForSlide(hasVisibleSlideText());
    }
}

function connectWebSocket() {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const ws = new WebSocket(`${protocol}//${location.host}/`);

    ws.onmessage = (event) => {
        try {
            handleMessage(JSON.parse(event.data));
        } catch {
            // ignore
        }
    };

    ws.onclose = () => {
        setTimeout(connectWebSocket, 1500);
    };
}

window.addEventListener('resize', () => {
    refitLayerText(layerA);
    refitLayerText(layerB);
});

loadInitialState().then(connectWebSocket);
