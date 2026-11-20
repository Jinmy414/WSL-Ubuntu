#!/bin/bash
# Install Windows proxy sharing for the selected Linux account.
set -euo pipefail

setup_dir=$(dirname "$(realpath "$0")")
user=${1:-${SUDO_USER:-}}
if [[ $EUID != 0 || -z "$user" ]]; then
  echo 'Usage: sudo bash install-host-network.sh <linux-user>' >&2
  exit 1
fi

uid=$(id -u -- "$user")
if [[ $uid == 0 ]]; then
  echo 'Choose the regular Linux account that runs the desktop.' >&2
  exit 1
fi

runtime_dir="/run/user/$uid"
if [[ ! -S "$runtime_dir/bus" ]]; then
  echo 'The Linux user manager is unavailable. Open a shell as that user and enable systemd in WSL.' >&2
  exit 1
fi

install -d -m 755 /usr/local/bin /usr/local/share/ubuntu-ds /etc/systemd/user
install -m 755 "$setup_dir/ds-proxy-watch" /usr/local/bin/ds-proxy-watch
install -m 644 "$setup_dir/ds-proxy-bash" /usr/local/share/ubuntu-ds/ds-proxy-bash
install -m 644 "$setup_dir/ds-proxy-watch.service" /etc/systemd/user/ds-proxy-watch.service

runuser -u "$user" -- bash -s <<'BASH'
set -euo pipefail
hook='/usr/local/share/ubuntu-ds/ds-proxy-bash'
rc_file="$HOME/.bashrc"
if [[ ! -f "$rc_file" ]] || ! grep -Fq "$hook" "$rc_file"; then
  printf '\n# Load the Ubuntu-DS Windows proxy environment.\n. %s\n' "$hook" >> "$rc_file"
fi
BASH

user_systemctl() {
  runuser -u "$user" -- env XDG_RUNTIME_DIR="$runtime_dir" \
    DBUS_SESSION_BUS_ADDRESS="unix:path=$runtime_dir/bus" systemctl --user "$@"
}

user_systemctl daemon-reload
user_systemctl enable ds-proxy-watch.service
user_systemctl restart ds-proxy-watch.service
echo 'Windows proxy synchronization installed.'
