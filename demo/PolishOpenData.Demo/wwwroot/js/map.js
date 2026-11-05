// index.html loads this module on its own, so the map is on screen before .NET starts; App.razor imports the same module afterwards.
const search = document.getElementById('search');
const radius = document.getElementById('radius');
const period = document.getElementById('period');

// If .NET never starts (a blocked or failed download) the box would say "Warming up…" for ever, so after 30 s it says so, also to a screen reader.
// It is set before anything else can throw. A start that is only very slow still ends in enableSearch, which puts the normal state back.
const startTimer = setTimeout(() => {
    search.placeholder = 'Could not start. Reload the page.';
    document.getElementById('start-status').textContent = 'The demo could not start. Reload the page.';
}, 30000);

const POLAND = [[49.0, 14.1], [54.9, 24.2]];
const ORTHOPHOTO = 'https://mapy.geoportal.gov.pl/wss/service/PZGIK/ORTO/WMTS/StandardResolution?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=ORTOFOTOMAPA&STYLE=default&FORMAT=image/jpeg&TILEMATRIXSET=EPSG:3857&TILEMATRIX=EPSG:3857:{z}&TILEROW={y}&TILECOL={x}';

// Quarter zoom steps let the first view show all of Poland on a phone, where level 6 is a little too wide (Poland is 460 px wide there).
// Leaflet draws the tile level nearest to the zoom, so 5.75 and 6.25 both draw level 6. The tile layer needs the same minimum as the
// map: below its own minZoom it drops every tile, and it compares that with the fractional zoom, not the tile level.
// Capping the first view at 6.25 keeps it to level 6 tiles on a desktop too.
const MIN_ZOOM = 5.75;
const map = L.map('map', { zoomControl: false, zoomSnap: 0.25, minZoom: MIN_ZOOM, maxZoom: 20 });
map.fitBounds(POLAND, { padding: [8, 8], maxZoom: 6.25 });
// Quarter steps were only for that first framing. From here the zoom buttons and the wheel move in whole levels, where the tiles are
// drawn at their native size (a quarter step would stretch a tile level by 1.19 and soften the photo). Leaflet reads this on every zoom.
map.options.zoomSnap = 1;
// Top right: the price card takes the bottom right corner. A phone hides the buttons (app.css) and zooms with two fingers.
L.control.zoom({ position: 'topright' }).addTo(map);

L.tileLayer(ORTHOPHOTO, {
    className: 'ortho',
    minZoom: MIN_ZOOM,
    maxNativeZoom: 19,
    maxZoom: 20,
    // The service answers HTTP 200 with an XML exception outside its tile matrix, so never ask outside Poland.
    bounds: [[48.8, 13.8], [55.0, 24.4]],
    attribution: 'Ortofotomapa &copy; <a href="https://www.geoportal.gov.pl/">GUGiK</a>',
}).addTo(map);

// ---- The static search bar, wired to the page (App.razor) once the .NET runtime is up ---------------------------------------------------
// Nothing here asks a service: Enter, a changed select and a changed address are handed to .NET, which decides. Typing sends nothing.

let page = null;

function toPage(method, ...args) {
    page?.invokeMethodAsync(method, ...args).catch(error => console.warn(method + ' failed', error));
}

// One way in: the form's submit, which a desktop Enter and a phone's Search or Go key both cause (an Enter that only ends an IME composition does not).
// A held Enter repeats its keydown and would submit each time, so a repeat is stopped; the same text again within a second (a double press) is
// dropped too. The page also joins a search already running for the same text, so neither reaches the geocoder twice.
const form = document.getElementById('search-form');
let lastSubmit = { text: null, time: -Infinity };

search.addEventListener('keydown', e => {
    if (e.key === 'Enter' && e.repeat) {
        e.preventDefault();
    }
});

form.addEventListener('submit', e => {
    e.preventDefault();
    const text = search.value.trim();
    const now = performance.now();
    if (search.disabled || (text === lastSubmit.text && now - lastSubmit.time < 1000)) {
        return;
    }

    lastSubmit = { text, time: now };
    // On a touch screen the keyboard would cover the card; with a mouse and keys the focus stays for the next search.
    if (matchMedia('(pointer: coarse)').matches) {
        search.blur();
    }

    toPage('OnSearchSubmit', search.value);
});
radius.addEventListener('change', () => toPage('OnRadiusChange', Number(radius.value)));
period.addEventListener('change', () => toPage('OnPeriodChange', Number(period.value)));
// Only a real change of the address (back, forward, a pasted link): history.replaceState below never fires it.
window.addEventListener('hashchange', () => toPage('OnHashChange', location.hash));

// The list of matching addresses (rendered by .NET as buttons in .candidates): arrows move between the box and the buttons, Escape closes it.
document.addEventListener('keydown', e => {
    const list = document.querySelector('.candidates');
    if (!list || (e.target !== search && !list.contains(e.target))) {
        return;
    }

    if (e.key === 'Escape') {
        e.preventDefault();
        search.focus();
        toPage('OnCandidatesClosed');
        return;
    }

    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') {
        return;
    }

    e.preventDefault();
    const items = [...list.querySelectorAll('button')];
    const at = items.indexOf(document.activeElement);
    const next = e.key === 'ArrowDown' ? items[Math.min(at + 1, items.length - 1)] : at <= 0 ? search : items[at - 1];
    next?.focus();
});

// Called by App.razor once the .NET runtime is up: turns the bar on with the page's choices and hands back the address's fragment, read once.
export function enableSearch(pageRef, radiusMeters, periodMonths) {
    clearTimeout(startTimer);
    page = pageRef;
    radius.value = String(radiusMeters);
    period.value = String(periodMonths);
    search.disabled = radius.disabled = period.disabled = false;
    search.placeholder = 'Search an address';
    return location.hash;
}

// After a candidate is chosen; not on a touch screen, where it would open the keyboard over the card.
export function focusSearch() {
    if (!matchMedia('(pointer: coarse)').matches) {
        search.focus();
    }
}

// Shows the radius and period the page uses (a link may have changed them).
export function setChoices(radiusMeters, periodMonths) {
    radius.value = String(radiusMeters);
    period.value = String(periodMonths);
}

// Mirrors the place into the address bar without navigating or adding a history entry; replaceState fires no hashchange.
export function setFragment(fragment) {
    if (location.hash !== fragment) {
        history.replaceState(history.state, '', location.pathname + location.search + fragment);
    }
}

// ---- The place on the map, driven from .NET (Map/MapInterop.cs) -------------------------------------------------------------------
// This part only draws shapes and forwards clicks. Everything that comes from the page (the outline, the numbers, the colours) is data:
// it is never turned into HTML, and nothing here opens a popup or a tooltip. What a dot means is shown by the page's own card.

const WHITE = '#fff';
const RED = '#ff1f3d';
const INK = '#1c2530';
const HEX = /^#[0-9a-f]{6}$/i;

// The circle and the outline sit in their own pane under the dots: the dots are drawn on a canvas in the overlay pane, the shapes
// on an SVG, and two layers of one pane stack by the order they were created in, which would depend on what was drawn first.
const placePane = map.createPane('place');
placePane.style.zIndex = 390;
const placeLayer = L.layerGroup().addTo(map);

// One canvas for every dot (about 1,000 of them), one click handler for the whole group; `index` on a dot's options says which one it was.
const dotRenderer = L.canvas({ tolerance: 3 });
const dotLayer = L.featureGroup().addTo(map);
dotLayer.on('click', e => forward('OnDotClick', e.layer.options.index));

// A dot that stands for several sales: it draws its number on the canvas right after its circle, in the same pass, so a number always sits
// with its own dot however the dots overlap (a separate marker layer would float every number above every dot). The number is a count
// turned into text by this script and drawn with fillText: nothing here is HTML. _updatePath, _order (the draw-order list) and _point are the
// internals of Leaflet 1.9.4 (vendored, so they cannot change under this).
const NumberedDot = L.CircleMarker.extend({
    _updatePath() {
        L.CircleMarker.prototype._updatePath.call(this);
        const renderer = this._renderer;
        if (!renderer._drawing || this._empty()) {
            return;
        }

        const ctx = renderer._ctx;
        const text = this.options.count > 99 ? '99+' : String(this.options.count);
        ctx.font = `700 ${text.length > 2 ? 9 : 11}px system-ui, -apple-system, "Segoe UI", Roboto, sans-serif`;

        // A dot that a later (so higher) dot covers across the width of its number shows no number: a sliver of a digit would read as another number.
        const reach = ctx.measureText(text).width / 2 + 1;
        for (let node = this._order.next; node; node = node.next) {
            const above = node.layer;
            if (above.options.fill && above._point.distanceTo(this._point) < above._radius + above.options.weight / 2 + reach) {
                return;
            }
        }

        ctx.globalAlpha = 1;
        ctx.fillStyle = this.options.textColor;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(text, this._point.x, this._point.y + 0.5);
    },
});

let dotNet = null;

function forward(method, ...args) {
    // The only way this fails is a callback that outlived the page's reference to .NET (a click while the page is being torn down).
    dotNet?.invokeMethodAsync(method, ...args).catch(error => console.warn(method + ' failed', error));
}

let lastPick = null;

function onMapClick(e) {
    // A dot's click does not get here: its markers have bubblingMouseEvents off.
    // A double click (or double tap) zooms in, as on any map, but it is two clicks: the second, close in time and place to the one just taken, is the same gesture.
    const now = performance.now();
    if (lastPick && now - lastPick.time < 400 && lastPick.point.distanceTo(e.containerPoint) < 10) {
        return;
    }

    lastPick = { time: now, point: e.containerPoint };
    const { lat, lng } = e.latlng.wrap();
    forward('OnMapClick', lat, lng);
}

// Starts forwarding clicks to the .NET object. Safe to call again: the handler is taken off before it is put on, so a click never counts twice.
export function attach(dotNetRef) {
    map.off('click', onMapClick);
    dotNet = dotNetRef;
    map.on('click', onMapClick);
}

// Stops forwarding and takes the place off the map. The map, its tiles and its view are left as they are.
export function detach() {
    map.off('click', onMapClick);
    dotNet = null;
    lastPick = null;
    clear();
}

export function clear() {
    focusLayer.clearLayers();
    placeLayer.clearLayers();
    dotLayer.clearLayers();
}

// center: [lat, lon]; parcelGeoJson: a GeoJSON geometry as text, or null; dots: [{ lat, lon, color, counted, count, index }].
export function showPlace(center, radiusMeters, parcelGeoJson, dots) {
    clear();
    const middle = L.latLng(center);

    // A faint dark line under the white dashes keeps the circle visible over pale roofs and paving.
    L.circle(middle, { pane: 'place', radius: radiusMeters, color: INK, weight: 4, opacity: 0.3, fill: false, interactive: false }).addTo(placeLayer);
    L.circle(middle, { pane: 'place', radius: radiusMeters, color: WHITE, weight: 2, dashArray: '6 6', fill: false, interactive: false }).addTo(placeLayer);
    if (parcelGeoJson) {
        try {
            L.geoJSON(JSON.parse(parcelGeoJson), { pane: 'place', style: { color: RED, weight: 3, fillColor: RED, fillOpacity: 0.12 }, interactive: false }).addTo(placeLayer);
        } catch (error) {
            // An outline that cannot be read is left out; the circle and the dots still show.
            console.warn('The parcel outline could not be drawn', error);
        }
    }

    drawDots(dots);
    // The circle's box rather than circle.getBounds(), which needs a view. zoomSnap is 1, so the fit may be a level looser than the box, never tighter.
    map.fitBounds(middle.toBounds(2 * radiusMeters), framePadding());
}

// The part of the map the floating search bar and the price card (app.css: bottom right on a desktop, the bottom 45 % on a phone) leave free.
function framePadding() {
    return innerWidth > 640
        ? { paddingTopLeft: [24, 24], paddingBottomRight: [400, 24] }
        : { paddingTopLeft: [12, 120], paddingBottomRight: [12, Math.round(innerHeight * 0.45) + 30] };
}

// A ring around the dot whose sales the card lists (index as in the dots; null for none); the map pans to it when it is out of the free part.
const focusPane = map.createPane('focus');
focusPane.style.zIndex = 450;
const focusLayer = L.layerGroup().addTo(map);

export function highlightDot(index) {
    focusLayer.clearLayers();
    let target = null;
    dotLayer.eachLayer(layer => {
        if (layer.options.index === index) {
            target = layer;
        }
    });
    if (index === null || !target) {
        return;
    }

    const at = target.getLatLng();
    const radius = target.options.radius + 5;
    L.circleMarker(at, { pane: 'focus', radius, color: INK, weight: 5, opacity: 0.45, fill: false, interactive: false }).addTo(focusLayer);
    L.circleMarker(at, { pane: 'focus', radius, color: WHITE, weight: 2.5, fill: false, interactive: false }).addTo(focusLayer);
    map.panInside(at, framePadding());
}

// Replaces the dots (and their numbers) and leaves the circle, the outline and the view alone.
export function showDots(dots) {
    drawDots(dots);
}

function drawDots(dots) {
    focusLayer.clearLayers();
    dotLayer.clearLayers();
    // Stacking, bottom to top, the same every time: the excluded dots under the counted ones, and within each group of them the dots with fewer sales
    // under those with more (then by index, the order of the result). So the dot that carries the most sales and a price is never covered by a neighbour.
    for (const dot of [...dots].sort((a, b) => (a.counted - b.counted) || (a.count - b.count) || (a.index - b.index))) {
        const position = [dot.lat, dot.lon];
        const color = HEX.test(dot.color) ? dot.color : '#6f7a89';
        const several = dot.count > 1;
        // Counted: filled with the class colour. The white edge keeps it readable on any photo.
        // Not counted: small and hollow, filled only when it has to carry a number.
        const style = dot.counted
            ? { radius: several ? 10 : 7, fillColor: color, fillOpacity: 0.95, color: WHITE, weight: 1.5, opacity: 0.9 }
            : { radius: several ? 9 : 5, fillColor: WHITE, fill: several, fillOpacity: 0.85, color, weight: 2, opacity: 1 };
        if (!dot.counted) {
            // A white casing under the grey ring, so the hollow dot shows on dark trees as well as on pale roofs.
            L.circleMarker(position, { renderer: dotRenderer, radius: style.radius, color: WHITE, weight: 5, opacity: 0.85, fill: false, interactive: false }).addTo(dotLayer);
        }

        const options = { ...style, renderer: dotRenderer, bubblingMouseEvents: false, index: dot.index };
        if (several) {
            // Dark text on a light dot, white on a dark one; on the white fill of a group that is not counted, dark.
            const textColor = dot.counted && luma(color) < 140 ? WHITE : INK;
            new NumberedDot(position, { ...options, count: dot.count, textColor }).addTo(dotLayer);
        } else {
            L.circleMarker(position, options).addTo(dotLayer);
        }
    }
}

function luma(hex) {
    const channel = i => parseInt(hex.slice(1 + 2 * i, 3 + 2 * i), 16);
    return 0.2126 * channel(0) + 0.7152 * channel(1) + 0.0722 * channel(2);
}
