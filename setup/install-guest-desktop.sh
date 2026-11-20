#!/bin/bash
set -euo pipefail

if [[ $EUID == 0 || ! -t 0 ]]; then
  echo 'Run this installer from an interactive WSL shell as the desktop user.' >&2
  exit 1
fi
if [[ $(< /proc/1/comm) != systemd ]]; then
  echo 'Enable systemd in /etc/wsl.conf and restart this WSL distribution before setup.' >&2
  exit 1
fi
if ! systemctl --user show-environment >/dev/null; then
  echo 'The systemd user session is unavailable. Reopen the WSL shell and try again.' >&2
  exit 1
fi

setup_dir=$(dirname -- "$(realpath -- "$0")")
unit_dir="$HOME/.config/systemd/user"
tls_dir="$HOME/.local/share/gnome-remote-desktop"

sudo apt-get update
sudo apt-get install --yes --no-install-recommends ubuntu-session gnome-remote-desktop \
  gnome-terminal nautilus dbus-user-session libgl1-mesa-dri openssl socat

sudo install -m 755 "$setup_dir/ds-headless-shell" /usr/local/bin/ds-headless-shell
sudo install -m 644 "$setup_dir/ds-rdp-v4.service" /etc/systemd/system/ds-rdp-v4.service
sudo loginctl enable-linger "$(id -un)"

install -d "$unit_dir/gnome-remote-desktop-headless.service.d"
cat > "$unit_dir/ds-shell-headless.service" <<'UNIT'
[Unit]
Description=Ubuntu-DS headless GNOME Shell

[Service]
ExecStart=/usr/local/bin/ds-headless-shell
Restart=on-failure
RestartSec=3
TimeoutStopSec=15

[Install]
WantedBy=default.target
UNIT
cat > "$unit_dir/gnome-remote-desktop-headless.service.d/10-ubuntu-ds.conf" <<'UNIT'
[Unit]
Wants=ds-shell-headless.service
After=ds-shell-headless.service

[Service]
Environment=GALLIUM_DRIVER=llvmpipe
Environment=LIBGL_ALWAYS_SOFTWARE=1
ExecStartPre=/bin/bash -c 'for ((i = 0; i < 60; i++)); do gdbus introspect --session --dest org.gnome.Mutter.RemoteDesktop --object-path /org/gnome/Mutter/RemoteDesktop >/dev/null 2>&1 && exit 0; sleep 1; done; exit 1'
UNIT

install -d -m 700 "$tls_dir"
if [[ ! -f "$tls_dir/ubuntu-ds.key" || ! -f "$tls_dir/ubuntu-ds.crt" ]]; then
  (umask 077; openssl req -x509 -newkey rsa:3072 -nodes -days 365 -subj '/CN=localhost' \
    -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' \
    -keyout "$tls_dir/ubuntu-ds.key" -out "$tls_dir/ubuntu-ds.crt")
fi
grdctl --headless rdp set-tls-key "$tls_dir/ubuntu-ds.key"
grdctl --headless rdp set-tls-cert "$tls_dir/ubuntu-ds.crt"
grdctl --headless rdp set-port 3391
grdctl --headless rdp disable-port-negotiation
echo 'Set a local RDP username and password. You will enter them in the Windows Remote Desktop prompt.'
grdctl --headless rdp set-credentials
grdctl --headless rdp disable-view-only

sudo bash "$setup_dir/install-host-network.sh" "$(id -un)"
systemctl --user daemon-reload
grdctl --headless rdp enable
systemctl --user enable ds-shell-headless.service
systemctl --user add-wants default.target gnome-remote-desktop-headless.service
systemctl --user start ds-shell-headless.service gnome-remote-desktop-headless.service
sudo systemctl daemon-reload
sudo systemctl enable --now ds-rdp-v4.service

echo 'Guest desktop is ready. Open the Windows console to connect to 127.0.0.1:3393.'
