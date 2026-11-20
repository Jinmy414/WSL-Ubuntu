#!/bin/bash
set -euo pipefail

usage() {
  printf '%s\n' \
    'Usage: bash setup/install-guest-desktop.sh [--check] [--reset-rdp-credentials] [--skip-clash]' \
    '  --check                  Check the guest without installing or changing settings.' \
    '  --reset-rdp-credentials  Set new RDP credentials during setup.' \
    '  --skip-clash             Skip installing the optional Clash desktop application.'
}

fail() {
  printf 'Setup stopped: %s\n' "$1" >&2
  exit 1
}

check_port() {
  local port=$1 scope=$2 service=$3 listeners
  listeners=$(ss -H -ltn "sport = :$port") || fail "Could not inspect TCP port $port."
  if [[ -z $listeners ]]; then
    printf '  Guest TCP %s is available.\n' "$port"
  elif systemctl "$scope" is-active --quiet "$service"; then
    printf '  Guest TCP %s has a listener and %s is active.\n' "$port" "$service"
  else
    fail "Guest TCP $port is already in use. Check the listener with ss -ltnp 'sport = :$port'."
  fi
}

preflight() {
  local network_mode ID='' VERSION_ID=''
  if [[ $EUID == 0 || ${WSL_DISTRO_NAME-} != Ubuntu-DS ]]; then
    fail 'Run this script as the default desktop user in the Ubuntu-DS WSL distribution.'
  fi
  . /etc/os-release
  if [[ $ID != ubuntu || $VERSION_ID != 26.04 ]]; then
    fail 'Use an Ubuntu 26.04 LTS distribution named Ubuntu-DS.'
  fi
  if ! network_mode=$(wslinfo --networking-mode 2>/dev/null) || [[ -z $network_mode || $network_mode == wsl1 ]]; then
    fail 'WSL 2 with wslinfo is required. In Windows, run wsl.exe --update and check wsl.exe --list --verbose.'
  fi
  if [[ $network_mode == none ]]; then
    fail 'Enable a WSL networking mode that supports Windows localhost forwarding before setup.'
  fi
  if [[ $(< /proc/1/comm) != systemd ]]; then
    fail 'Set systemd=true under [boot] in /etc/wsl.conf, then restart Ubuntu-DS.'
  fi
  if ! systemctl --user show-environment >/dev/null 2>&1; then
    fail 'The systemd user manager is unavailable. Reopen Ubuntu-DS as its default user.'
  fi
  command -v sudo >/dev/null || fail 'The sudo command is required for installing system packages.'
  command -v ss >/dev/null || fail 'Install the iproute2 package so setup can check the local ports.'
  command -v python3 >/dev/null || fail 'Install python3 to enable proxy-aware setup.'
  printf '  Ubuntu %s LTS on WSL 2, network mode %s.\n' "$VERSION_ID" "$network_mode"
  check_port 3391 --user gnome-remote-desktop-headless.service
  check_port 3393 --system ds-rdp-v4.service
}

rdp_credential_state() {
  local status
  status=$(LC_ALL=C grdctl --headless status 2>&1) || return 2
  if [[ $status == *'Failed to read credentials:'* ]]; then return 2; fi
  awk '
    /^RDP:$/ { rdp = 1; next }
    /^[^[:space:]]/ { rdp = 0 }
    rdp && $1 == "Username:" { user = $2 }
    rdp && $1 == "Password:" { password = $2 }
    END {
      if (user == "(hidden)" && password == "(hidden)") exit 0
      if (user == "(empty)" && password == "(empty)") exit 1
      exit 2
    }
  ' <<< "$status"
}

check_only=false
reset_credentials=false
skip_clash=false
for argument in "$@"; do
  case $argument in
    --check) check_only=true ;;
    --reset-rdp-credentials) reset_credentials=true ;;
    --skip-clash) skip_clash=true ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; exit 2 ;;
  esac
done

echo '[1/4] Check the WSL guest and desktop ports'
preflight
if $check_only; then
  echo 'Guest preflight passed. Run setup without --check to install the desktop.'
  exit 0
fi
if [[ ! -t 0 ]]; then fail 'Run setup in an interactive WSL shell so it can prompt for credentials.'; fi

setup_dir=$(dirname -- "$(realpath -- "$0")")
unit_dir="$HOME/.config/systemd/user"
tls_dir="$HOME/.local/share/gnome-remote-desktop"
tls_key="$tls_dir/ubuntu-ds.key"
tls_cert="$tls_dir/ubuntu-ds.crt"

if [[ -e $tls_key && ! -f $tls_cert || -e $tls_cert && ! -f $tls_key ]]; then
  fail 'Only one file in the project TLS pair exists. Restore its matching file or move the remaining file before retrying.'
fi

echo '[2/4] Install GNOME, Remote Desktop and the local relay'
apt_via_windows() {
  # Bootstrap package downloads before the guest Clash application is configured.
  python3 "$setup_dir/ds-proxy-watch" --exec-windows \
    sudo --preserve-env=http_proxy,https_proxy,no_proxy apt-get "$@"
}
apt_via_windows update
apt_via_windows install --yes --no-install-recommends ubuntu-session gnome-remote-desktop \
  gnome-terminal nautilus dbus-user-session libgl1-mesa-dri curl openssl socat

echo '[3/4] Configure RDP and proxy synchronization'
credential_state=0
if ! $reset_credentials; then
  rdp_credential_state || credential_state=$?
  if [[ $credential_state == 2 ]]; then
    fail 'Could not verify the credential state. To explicitly set new credentials, rerun with --reset-rdp-credentials.'
  fi
fi
if $reset_credentials || [[ $credential_state == 1 ]]; then
  echo 'Set an RDP username and password for Windows Remote Desktop. Use more than one character for each.'
  grdctl --headless rdp set-credentials
  rdp_credential_state || fail 'The RDP username and password must both contain more than one character. Rerun with --reset-rdp-credentials.'
else
  echo '  Using the existing GNOME Remote Desktop credentials.'
fi

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
if [[ ! -f $tls_key && ! -f $tls_cert ]]; then
  (
    umask 077
    pending=$(mktemp -d "$tls_dir/.ubuntu-ds-tls.XXXXXXXX")
    trap 'rm -f -- "$pending/key" "$pending/cert"; rmdir -- "$pending"' EXIT
    openssl req -x509 -newkey rsa:3072 -nodes -days 365 -subj '/CN=localhost' \
      -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' \
      -keyout "$pending/key" -out "$pending/cert"
    mv -- "$pending/key" "$tls_key"
    mv -- "$pending/cert" "$tls_cert"
  )
fi
grdctl --headless rdp set-tls-key "$tls_key"
grdctl --headless rdp set-tls-cert "$tls_cert"
grdctl --headless rdp set-port 3391
grdctl --headless rdp disable-port-negotiation
grdctl --headless rdp disable-view-only

sudo bash "$setup_dir/install-host-network.sh" "$(id -un)"
if $skip_clash; then
  if [[ ! -e /etc/ubuntu-ds/proxy-mode ]]; then
    sudo /usr/local/bin/ds-proxy-watch --mode windows
  fi
elif ! bash "$setup_dir/install-clash.sh"; then
  echo 'Clash installation did not finish. To retry, run bash setup/install-clash.sh in Ubuntu.' >&2
fi
echo '[4/4] Start the desktop and RDP relay'
systemctl --user daemon-reload
grdctl --headless rdp enable
systemctl --user enable ds-shell-headless.service
systemctl --user add-wants default.target gnome-remote-desktop-headless.service
systemctl --user start ds-shell-headless.service gnome-remote-desktop-headless.service
sudo systemctl daemon-reload
sudo systemctl enable --now ds-rdp-v4.service
systemctl --user is-active --quiet gnome-remote-desktop-headless.service || \
  fail 'GNOME Remote Desktop did not start. Run bin\ds-doctor.cmd in Windows for diagnostics.'
systemctl is-active --quiet ds-rdp-v4.service || \
  fail 'The RDP relay did not start. Run bin\ds-doctor.cmd in Windows for diagnostics.'

echo 'Guest services are running. From Windows, finish setup with setup\install-windows.ps1.'
echo 'Open the Ubuntu-DS Console shortcut and connect to 127.0.0.1:3393 with the GNOME RDP credentials.'
if ! $skip_clash; then
  echo 'The guest proxy source defaults to Clash. Open Clash Verge and import your own configuration.'
fi
echo 'For the first Windows certificate prompt, compare the certificate fingerprint shown below.'
openssl x509 -in "$tls_cert" -noout -fingerprint -sha1
