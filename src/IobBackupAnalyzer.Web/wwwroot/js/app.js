// Die schmale Brücke zum Browser: alles, was .NET im Web nicht selbst kann.
//
// Bewusst klein gehalten. Jede Zeile hier ist eine Zeile, die es in den beiden
// Desktop-Fassungen nicht gibt und die deshalb auch nirgends mitgetestet wird — die
// Auswertung selbst läuft vollständig in C# und ist damit dieselbe wie dort.

window.iobAnalyzer = {

    // ---------------------------------------------------------------- Herunterladen

    /**
     * Reicht eine in .NET erzeugte Datei an den Browser weiter.
     *
     * Der Umweg über den Datenstrom statt über eine Base64-Zeichenkette ist kein
     * Schönheitsfehler: Ein Skript-Export kann etliche Megabyte groß sein, und als Text
     * kodiert läge er dreimal gleichzeitig im Speicher — einmal in .NET, einmal als
     * Zeichenkette, einmal wieder als Bytes.
     */
    async speichern(dateiname, streamRef, mimeTyp) {
        const puffer = await streamRef.arrayBuffer();
        const blob = new Blob([puffer], { type: mimeTyp || 'application/octet-stream' });
        this._anbieten(blob, dateiname);
    },

    /**
     * Kurzer Text (CSV, Skript, Protokoll) — hier lohnt der Datenstrom nicht.
     *
     * mitBom entscheidet über den Byte-Order-Mark, und das ist keine Kleinigkeit: Eine
     * CSV braucht ihn, sonst zeigt Excel falsche Umlaute. In ein Shell-Skript darf er auf
     * keinen Fall — er stünde vor der Shebang-Zeile und entwertete sie. Dieselbe
     * Unterscheidung treffen die Desktop-Fassungen in ScriptExporter.WriteCsv und
     * CleanupScriptGenerator.ForFile.
     */
    speichernText(dateiname, text, mimeTyp, mitBom) {
        const teile = mitBom ? ['﻿', text] : [text];
        const blob = new Blob(teile, { type: (mimeTyp || 'text/plain') + ';charset=utf-8' });
        this._anbieten(blob, dateiname);
    },

    /** Gemeinsamer Weg vom Blob zum Download-Ordner. */
    _anbieten(blob, dateiname) {
        const url = URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.href = url;
        a.download = dateiname;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);

        // Erst freigeben, wenn der Browser den Download übernommen hat. Sofortiges
        // Freigeben bricht ihn in manchen Browsern ab.
        setTimeout(() => URL.revokeObjectURL(url), 30000);
    },

    /**
     * In die Zwischenablage. Liefert false, wenn beide Wege versperrt sind.
     *
     * Der erste Weg ist der heutige (navigator.clipboard) — er steht aber nur auf
     * gesicherten Seiten zur Verfügung. Ein ioBroker im Heimnetz liefert typischerweise
     * über http aus, und dort ist er schlicht nicht vorhanden.
     *
     * Deshalb der zweite Weg: ein unsichtbares Textfeld, dessen Inhalt markiert und über
     * das alte execCommand kopiert wird. Das gilt als veraltet, funktioniert aber in jedem
     * Browser und ohne https — und ist damit hier der wichtigere von beiden.
     */
    async kopieren(text) {
        try {
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }
        } catch {
            // Weiter mit dem zweiten Weg.
        }

        try {
            const feld = document.createElement('textarea');
            feld.value = text;

            // Außerhalb des Bildes, aber nicht versteckt: Ein Feld mit display:none lässt
            // sich nicht markieren, und ohne Markierung kopiert execCommand nichts.
            feld.style.position = 'fixed';
            feld.style.top = '-1000px';
            feld.setAttribute('readonly', '');

            document.body.appendChild(feld);
            feld.select();
            const geklappt = document.execCommand('copy');
            document.body.removeChild(feld);
            return geklappt;
        } catch {
            return false;
        }
    },

    /**
     * Rollt eine Tabelle an eine bestimmte Zeile.
     *
     * Nötig, weil die Tabelle nur die sichtbaren Zeilen erzeugt: Ein Sprung zu einem
     * Skript, das gerade nicht im Bild ist, wählt sonst eine Zeile aus, die niemand sieht.
     * Gerechnet statt gesucht — die Zeilenhöhe ist fest, und die gesuchte Zeile gibt es
     * im Dokument noch gar nicht.
     */
    zuZeileRollen(rahmenOderWahl, index, zeilenhoehe) {
        // Zwei Wege hinein: ein Element von Blazor oder ein Auswahlausdruck. Der zweite
        // ist der verlässlichere — er greift das Element in dem Moment, in dem gerollt
        // wird, und nicht das, was beim ersten Zeichnen dort stand.
        const start = typeof rahmenOderWahl === 'string'
            ? document.querySelector(rahmenOderWahl)
            : rahmenOderWahl;

        if (!start) return;

        // Das rollende Element suchen, statt es zu erraten.
        //
        // Der Rahmen der Tabelle rollt selbst nicht — das tut sein Elternelement, der
        // Bereich, dem das Layout eine feste Höhe gibt. Wer stattdessen die Tabelle
        // selbst rollt, setzt scrollTop an einem Element, das gar keinen Überhang hat:
        // Der Browser nimmt den Wert stillschweigend nicht an, und nichts bewegt sich.
        // Genau daran scheiterte der Sprung aus dem Reiter „Verwendung".
        const rollend = (el) => {
            for (let e = el; e && e !== document.body; e = e.parentElement) {
                const art = getComputedStyle(e).overflowY;
                if (art === 'auto' || art === 'scroll') return e;
            }
            return el;
        };

        const rahmen = rollend(start);

        // Drei Zeilen Vorlauf, damit die Zeile nicht am oberen Rand klebt.
        const ziel = Math.max(0, (index - 3) * zeilenhoehe);

        // Nachfassen, und das ist der eigentliche Kniff: Beim ersten Versuch steht die
        // Tabelle gerade erst im Dokument und ist nur so hoch wie ihre wenigen erzeugten
        // Zeilen. Der Browser begrenzt scrollTop auf das, was dann möglich ist — also
        // meist auf null, und der Sprung landet sichtbar nirgends.
        //
        // Sobald die Tabelle ihre Gesamthöhe kennt, greift derselbe Wert. Deshalb wird
        // er mehrfach gesetzt, bis er stehen bleibt oder die Geduld endet.
        // Gewartet wird auf einen Zustand, nicht auf eine Frist.
        //
        // Unmittelbar nach dem Reiterwechsel ist der Rahmen so hoch wie sein Inhalt und
        // damit gar nicht rollbar (scrollHeight === clientHeight). Ein scrollTop, das
        // dann gesetzt wird, verwirft der Browser stillschweigend. Erst wenn das
        // Flex-Layout greift, bekommt der Rahmen seine begrenzte Höhe — und erst dann
        // lässt sich rollen. Nachgemessen an einem Backup mit 193 Skripten: bis dahin
        // vergingen mehr als zwei Sekunden.
        //
        // Nach der Uhr allein zu zählen genügte deshalb nicht; hier wird gewartet, bis
        // der Rahmen rollbar ist, und danach so lange nachgesetzt, bis der Wert steht.
        let versuche = 0;
        let gehalten = 0;

        const setzen = () => {
            const rollbar = rahmen.scrollHeight > rahmen.clientHeight + 1;

            if (rollbar) {
                rahmen.scrollTop = ziel;

                // Zweimal hintereinander erreicht: Erst dann ist sicher, dass die Tabelle
                // den Wert nicht beim nächsten Neuaufbau wieder verwirft.
                if (Math.abs(rahmen.scrollTop - ziel) < 2 && ++gehalten >= 2) return;
            }

            // Rund fünf Sekunden Geduld. Danach steht die Auswahl zwar außerhalb des
            // Bildes, aber niemand wartet auf ein Rollen, das nicht mehr kommt.
            if (++versuche > 100) return;

            setTimeout(setzen, 50);
        };

        setzen();
    },

    // ---------------------------------------------------------------- Einstellungen

    lesen(schluessel) {
        try { return localStorage.getItem(schluessel); } catch { return null; }
    },

    schreiben(schluessel, wert) {
        // Im privaten Fenster mancher Browser wirft schon der Zugriff. Einstellungen sind
        // Komfort — ein Fehler hier darf die Seite nicht anhalten.
        try { localStorage.setItem(schluessel, wert); } catch { /* ohne Merken weiter */ }
    },

    // ---------------------------------------------------------------- Darstellung

    /** "system", "hell" oder "dunkel" — das Stylesheet wertet das Attribut aus. */
    themaSetzen(name) {
        document.documentElement.setAttribute('data-theme', name);
    },

    // ---------------------------------------------------------------- Sonstiges

    /**
     * Der Speicherstand des Reiters in Megabyte, oder null.
     *
     * Nur Chrome und Edge liefern das. Der Wert steht in der Fußzeile, weil die
     * WebAssembly-Laufzeit 32-bittig ist: Bei sehr großen Backups ist die Grenze von rund
     * 2 GB erreichbar, und dann bricht der Reiter ohne Vorwarnung ab. Wer den Wert steigen
     * sieht, weiß wenigstens, woran es lag.
     */
    speicherStand() {
        const m = performance && performance.memory;
        if (!m || !m.usedJSHeapSize) return null;
        return Math.round(m.usedJSHeapSize / 1048576);
    },

    /**
     * Browser und Hauptversion, für den Kopf des Ladeprotokolls.
     *
     * Bewusst nicht die vollständige Browser-Kennung: Die nennt auch die genaue Version
     * des Betriebssystems und mitunter das Gerätemodell. Das Protokoll soll weitergegeben
     * werden können, ohne dass man es vorher durchsehen muss — also steht dort nur, was
     * bei der Fehlersuche wirklich hilft. Safari und Firefox verhalten sich an einigen
     * Stellen anders als Chrome, und genau das will man wissen.
     */
    browserKennung() {
        const ua = navigator.userAgent || '';

        // Reihenfolge zählt: Edge und Opera nennen sich zusätzlich „Chrome", Chrome
        // nennt sich zusätzlich „Safari".
        const muster = [
            [/Edg\/(\d+)/, 'Edge'],
            [/OPR\/(\d+)/, 'Opera'],
            [/Firefox\/(\d+)/, 'Firefox'],
            [/Chrome\/(\d+)/, 'Chrome'],
            [/Version\/(\d+).*Safari/, 'Safari']
        ];

        for (const [regel, name] of muster) {
            const treffer = ua.match(regel);
            if (treffer) return name + ' ' + treffer[1];
        }

        return 'unbekannter Browser';
    },

    // ---------------------------------------------------------------- Trennlinie ziehen

    /**
     * Macht die Linie zwischen zwei übereinanderliegenden Bereichen verschiebbar — das
     * Gegenstück zum SplitContainer der Windows-Fassung und zum GridSplitter der
     * Avalonia-Fassung.
     *
     * Der Griff nimmt sich seine Nachbarn selbst: darüber der eine Bereich, darunter der
     * andere. Deshalb genügt es, die Komponente zwischen beide zu setzen.
     *
     * Die Untergrenzen sind kein Beiwerk. Ohne sie liesse sich der obere Bereich auf null
     * ziehen — und dann käme der Sprung aus dem Reiter „Verwendung" zwar an der richtigen
     * Zeile an, nur sähe man sie nicht. Beide Desktop-Fassungen setzen aus demselben Grund
     * 120 Pixel als Minimum.
     *
     * Warum Zeigerereignisse und nicht Mausereignisse: Sie decken Maus, Finger und Stift
     * ab. setPointerCapture sorgt dafür, dass das Ziehen weiterläuft, wenn der Zeiger den
     * schmalen Griff verlässt — ohne das reisst es bei jeder schnellen Bewegung ab.
     */
    trennerEinrichten(griff, minOben, minUnten) {
        const oben = griff.previousElementSibling;
        const unten = griff.nextElementSibling;
        if (!oben || !unten) return;

        let startY = 0;
        let startHoehe = 0;

        griff.addEventListener('pointerdown', e => {
            startY = e.clientY;
            startHoehe = oben.getBoundingClientRect().height;
            griff.setPointerCapture(e.pointerId);
            griff.classList.add('zieht');
            e.preventDefault();          // sonst markiert der Zug den Text daneben
        });

        griff.addEventListener('pointermove', e => {
            if (!griff.hasPointerCapture(e.pointerId)) return;

            // Die Gesamthöhe jedes Mal neu messen: Das Fenster kann sich zwischendurch
            // geändert haben, und ein veralteter Wert liesse den unteren Bereich unter
            // seine Grenze rutschen.
            const gesamt = oben.getBoundingClientRect().height
                         + unten.getBoundingClientRect().height;

            let neu = startHoehe + (e.clientY - startY);
            neu = Math.max(minOben, Math.min(neu, gesamt - minUnten));

            // Erst hier wird aus dem Verhältnis (flex: 3 zu 2) eine feste Höhe. Solange
            // niemand zieht, bleibt das ursprüngliche Layout unangetastet und passt sich
            // der Fenstergrösse an.
            oben.style.flex = '0 0 ' + Math.round(neu) + 'px';
            unten.style.flex = '1 1 0';
        });

        const loslassen = e => {
            if (griff.hasPointerCapture(e.pointerId)) griff.releasePointerCapture(e.pointerId);
            griff.classList.remove('zieht');
        };

        griff.addEventListener('pointerup', loslassen);
        griff.addEventListener('pointercancel', loslassen);
    }
};

// Der Fehlerstreifen von Blazor lässt sich wegklicken.
document.addEventListener('click', e => {
    if (e.target && e.target.classList.contains('dismiss')) {
        document.getElementById('blazor-error-ui').style.display = 'none';
    }
});
