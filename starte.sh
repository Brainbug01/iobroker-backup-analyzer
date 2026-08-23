#!/bin/sh
#
# ioBroker Backup Analyzer — Startskript für Linux
#
# Wird von build.ps1 in das Linux-Paket gelegt und dort einfach "starte.sh" genannt.
#
# Warum es das gibt: Das Programm bringt seine eigene .NET-Laufzeit mit, aber zwei
# Bibliotheken muss das System stellen — ICU (Zahlen-, Datums- und Sortierregeln) und
# fontconfig (Schriftverwaltung). Auf einem Desktop-Linux sind beide ohnehin da. Auf einem
# schlanken oder frisch aufgesetzten System fehlen sie, und genau so sieht ein typischer
# ioBroker-Rechner aus.
#
# Ohne dieses Skript bricht das Programm dann mit einem englischen .NET-Stapelabzug ab, aus
# dem ein normaler Anwender nicht schließen kann, was zu tun ist. Deshalb wird hier vorher
# geprüft und im Klartext gesagt, welcher Befehl fehlt.

set -u

verzeichnis=$(cd "$(dirname "$0")" && pwd)
programm="$verzeichnis/ioBroker-Backup-Analyzer"

if [ ! -x "$programm" ]; then
    echo "Das Programm wurde neben diesem Skript nicht gefunden:" >&2
    echo "  $programm" >&2
    echo "" >&2
    echo "Bitte das Archiv vollständig entpacken und starte.sh im entpackten Ordner lassen." >&2
    exit 1
fi

# ldconfig listet die bekannten Bibliotheken auf. Es liegt in /sbin, das bei normalen
# Benutzern nicht immer im Suchpfad steht — daher beide Wege. Bringt keiner davon etwas
# (etwa auf einem System ohne glibc-Werkzeuge), wird die Prüfung übersprungen: lieber
# ungeprüft starten als fälschlich blockieren.
bibliotheken=$( { /sbin/ldconfig -p || ldconfig -p; } 2>/dev/null || true )

if [ -n "$bibliotheken" ]; then
    fehlt_icu=0
    fehlt_fontconfig=0

    echo "$bibliotheken" | grep -q 'libicuuc\.so' || fehlt_icu=1
    echo "$bibliotheken" | grep -q 'libfontconfig\.so' || fehlt_fontconfig=1

    if [ "$fehlt_icu" -eq 1 ] || [ "$fehlt_fontconfig" -eq 1 ]; then
        # Der ICU-Paketname trägt die Hauptversion im Namen und unterscheidet sich je nach
        # Distribution (libicu72, libicu74, libicu76 …). Statt zu raten, wird der auf diesem
        # System gültige Name ermittelt, damit unten ein Befehl steht, der wirklich läuft.
        icu_paket=$(apt-cache search --names-only '^libicu[0-9][0-9]*$' 2>/dev/null \
                    | cut -d' ' -f1 | sort -V | tail -n 1)
        [ -z "$icu_paket" ] && icu_paket="libicu"

        pakete=""
        [ "$fehlt_icu" -eq 1 ] && pakete="$pakete $icu_paket"
        [ "$fehlt_fontconfig" -eq 1 ] && pakete="$pakete libfontconfig1"

        echo ""
        echo "  Es fehlen noch Systembibliotheken."
        echo ""
        echo "  Der ioBroker Backup Analyzer bringt seine Laufzeit selbst mit, braucht aber"
        [ "$fehlt_icu" -eq 1 ] && \
        echo "  ICU (Zahlen- und Sortierregeln)"
        [ "$fehlt_fontconfig" -eq 1 ] && \
        echo "  fontconfig (Schriftverwaltung)"
        echo "  vom System."
        echo ""
        echo "  Auf Debian und Ubuntu genügt dieser Befehl:"
        echo ""
        echo "      sudo apt install$pakete"
        echo ""
        echo "  Auf Fedora:        sudo dnf install libicu fontconfig"
        echo "  Auf openSUSE:      sudo zypper install libicu fontconfig"
        echo "  Auf Arch Linux:    sudo pacman -S icu fontconfig"
        echo ""
        echo "  Danach dieses Skript erneut aufrufen."
        echo ""
        exit 1
    fi
fi

# Eine grafische Oberfläche braucht es außerdem. Ohne DISPLAY oder WAYLAND_DISPLAY bricht
# das Programm sonst mit "XOpenDisplay failed" ab — auf einem Server ohne Bildschirm der
# wahrscheinlichste Fall.
if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    echo ""
    echo "  Keine grafische Oberfläche gefunden."
    echo ""
    echo "  Der ioBroker Backup Analyzer ist ein Fenster-Programm und braucht einen"
    echo "  Bildschirm. Auf einem Server ohne Oberfläche gibt es drei Wege:"
    echo ""
    echo "    - Das Backup auf einen Rechner mit Oberfläche kopieren und dort öffnen."
    echo "    - Per SSH mit X-Weiterleitung verbinden:  ssh -X benutzer@rechner"
    echo "    - Eine Oberfläche nachrüsten, etwa XFCE zusammen mit einem Fernzugang."
    echo ""
    exit 1
fi

exec "$programm" "$@"
