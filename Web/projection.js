// Состояние слайдов
let currentLayer = 'a';
let transitionStyle = 'FadeSlide';
let transitionDurationMs = 750;

// Функция для отправки логов в C#
function logToCSharp(message) {
    if (window.chrome?.webview) {
        window.chrome.webview.postMessage(JSON.stringify({ type: 'log', message }));
    }
}

// Элементы
const layerA = document.getElementById('layer-a');
const layerB = document.getElementById('layer-b');
const background = document.getElementById('background');

// Инициализация
if (layerA && layerB) {
    layerA.style.opacity = '1';
    layerB.style.opacity = '0';
    logToCSharp('[projection.js] Initialized layers');
    console.log('[projection.js] Initialized layers');
} else {
    logToCSharp('[projection.js] Layers not found!');
    console.error('[projection.js] Layers not found!');
}

// Приём сообщений от C#
if (window.chrome?.webview) {
    window.chrome.webview.addEventListener('message', (e) => {
        logToCSharp('[projection.js] Received message: ' + e.data);
        console.log('[projection.js] Received message:', e.data);
        try {
            const data = JSON.parse(e.data);
            handleMessage(data);
        } catch (ex) {
            logToCSharp('[projection.js] Failed to parse message: ' + ex.message);
            console.error('[projection.js] Failed to parse message:', ex);
        }
    });
    logToCSharp('[projection.js] Webview message listener attached');
    console.log('[projection.js] Webview message listener attached');
} else {
    logToCSharp('[projection.js] window.chrome.webview not available');
    console.error('[projection.js] window.chrome.webview not available');
}

function handleMessage(data) {
    switch (data.type) {
        case 'updateSlide':
            updateSlide(data);
            break;
        case 'updateBackground':
            updateBackground(data);
            break;
        case 'updateTheme':
            updateTheme(data);
            break;
        case 'setTransitionStyle':
            transitionStyle = data.style;
            break;
        case 'setTransitionDuration':
            transitionDurationMs = data.durationMs;
            break;
    }
}

function updateSlide(data) {
    const { lines, referenceCaption, transitionStyle: style } = data;
    
    if (style) {
        transitionStyle = style;
    }

    const currentEl = currentLayer === 'a' ? layerA : layerB;
    const nextEl = currentLayer === 'a' ? layerB : layerA;

    // Заполняем следующий слой новым контентом
    fillLayer(nextEl, lines, referenceCaption);

    // Сбрасываем стили следующего слоя
    nextEl.style.transition = 'none';
    nextEl.style.opacity = '0';
    nextEl.style.transform = 'none';
    nextEl.style.filter = 'none';

    // Принудительный reflow
    void nextEl.offsetHeight;

    // Запускаем анимацию
    runTransition(currentEl, nextEl, transitionStyle, transitionDurationMs);
}

function fillLayer(el, lines, referenceCaption) {
    let html = '';
    
    if (referenceCaption) {
        html += `<div class="reference">${escapeHtml(referenceCaption)}</div>`;
    }
    
    lines.forEach((line, index) => {
        const isFirst = index === 0;
        html += `<div class="line" style="${isFirst ? '' : 'color: #c9cedb; font-size: 0.85em;'}">${escapeHtml(line)}</div>`;
    });
    
    el.innerHTML = html;
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

async function runTransition(currentEl, nextEl, style, durationMs) {
    switch (style) {
        case 'Fade':
            await runFade(currentEl, nextEl, durationMs);
            break;
        case 'FadeSlide':
            await runFadeSlide(currentEl, nextEl, durationMs);
            break;
        case 'BlurSharp':
            await runBlurSharp(currentEl, nextEl, durationMs);
            break;
        case 'Stagger':
            await runStagger(currentEl, nextEl, durationMs);
            break;
        default:
            await runFadeSlide(currentEl, nextEl, durationMs);
    }

    // Переключаем активный слой
    currentLayer = currentLayer === 'a' ? 'b' : 'a';
}

// 01: Fade - простой fade через один слой
async function runFade(currentEl, nextEl, durationMs) {
    const halfDuration = durationMs / 2;
    
    currentEl.style.transition = `opacity ${halfDuration}ms cubic-bezier(0.4, 0, 0.2, 1)`;
    currentEl.style.opacity = '0';
    
    await sleep(halfDuration);
    
    nextEl.style.transition = `opacity ${halfDuration}ms cubic-bezier(0.4, 0, 0.2, 1)`;
    nextEl.style.opacity = '1';
    
    await sleep(halfDuration);
    
    currentEl.style.transform = 'none';
}

// 03: Fade + Slide - два слоя + вертикальное движение
async function runFadeSlide(currentEl, nextEl, durationMs) {
    const easing = `cubic-bezier(0.22, 0.61, 0.36, 1)`;
    
    currentEl.style.transition = `opacity ${durationMs}ms ${easing}, transform ${durationMs}ms ${easing}`;
    nextEl.style.transition = `opacity ${durationMs}ms ${easing}, transform ${durationMs}ms ${easing}`;
    
    currentEl.style.opacity = '0';
    currentEl.style.transform = 'translateY(-14px)';
    
    nextEl.style.opacity = '1';
    nextEl.style.transform = 'translateY(18px)';
    nextEl.style.transform = 'translateY(0)';
    
    await sleep(durationMs);
    
    currentEl.style.transform = 'none';
}

// 05: Blur → Sharp
async function runBlurSharp(currentEl, nextEl, durationMs) {
    const easing = 'ease-out';
    
    currentEl.style.transition = `opacity ${durationMs}ms ${easing}, filter ${durationMs}ms ${easing}`;
    nextEl.style.transition = `opacity ${durationMs}ms ${easing}, filter ${durationMs}ms ${easing}`;
    
    currentEl.style.opacity = '0';
    currentEl.style.filter = 'blur(6px)';
    
    nextEl.style.opacity = '1';
    nextEl.style.filter = 'blur(10px)';
    nextEl.style.filter = 'blur(0)';
    
    await sleep(durationMs);
    
    currentEl.style.filter = 'none';
}

// 07: Line-by-line Stagger
async function runStagger(currentEl, nextEl, durationMs) {
    const stagger = 90;
    const easing = `cubic-bezier(0.22, 0.61, 0.36, 1)`;
    
    const currentLines = currentEl.querySelectorAll('.line');
    const nextLines = nextEl.querySelectorAll('.line');
    
    // Показываем следующий слой целиком, но строки скрыты
    nextEl.style.opacity = '1';
    nextEl.style.transition = 'none';
    
    nextLines.forEach(l => {
        l.style.transition = 'none';
        l.style.opacity = '0';
        l.style.transform = 'translateY(14px)';
    });
    
    void nextEl.offsetHeight;
    
    // Анимируем строки текущего слоя (исчезают)
    currentLines.forEach((l, idx) => {
        l.style.transition = `opacity ${durationMs}ms ${easing}, transform ${durationMs}ms ${easing}`;
        setTimeout(() => {
            l.style.opacity = '0';
            l.style.transform = 'translateY(-10px)';
        }, idx * stagger);
    });
    
    // Анимируем строки следующего слоя (появляются)
    nextLines.forEach((l, idx) => {
        l.style.transition = `opacity ${durationMs}ms ${easing}, transform ${durationMs}ms ${easing}`;
        setTimeout(() => {
            l.style.opacity = '1';
            l.style.transform = 'translateY(0)';
        }, idx * stagger);
    });
    
    await sleep(durationMs + (nextLines.length - 1) * stagger);
    
    currentLines.forEach(l => {
        l.style.transform = 'none';
    });
}

function sleep(ms) {
    return new Promise(resolve => setTimeout(resolve, ms));
}

function updateBackground(data) {
    const { color, imageUrl } = data;
    
    if (imageUrl) {
        background.style.backgroundImage = `url(${imageUrl})`;
    } else if (color) {
        background.style.backgroundImage = 'none';
        background.style.backgroundColor = color;
    }
}

function updateTheme(data) {
    const { 
        primaryColor, 
        fontSize, 
        fontFamily,
        textOutlineColor,
        textOutlineThickness
    } = data;
    
    const lines = document.querySelectorAll('.line, .reference');
    
    if (primaryColor) {
        lines.forEach(el => {
            el.style.color = primaryColor;
        });
    }
    
    if (fontSize) {
        lines.forEach(el => {
            el.style.fontSize = `${fontSize}px`;
        });
    }
    
    if (fontFamily) {
        lines.forEach(el => {
            el.style.fontFamily = fontFamily;
        });
    }
    
    if (textOutlineColor && textOutlineThickness) {
        lines.forEach(el => {
            el.style.textShadow = `0 0 ${textOutlineThickness}px ${textOutlineColor}, 0 2px 14px rgba(0,0,0,0.55)`;
        });
    }
}
