#!/bin/bash
set -euo pipefail

usage() {
  printf '%s\n' 'Usage: bash setup/install-clash.sh [--check]' \
    'Install Clash Verge Rev from the official release, or check whether it is installed.'
}

if (( $# > 1 )); then usage >&2; exit 2; fi
case ${1-} in
  ''|--check) ;;
  -h|--help) usage; exit 0 ;;
  *) usage >&2; exit 2 ;;
esac

if [[ $(dpkg-query -W -f='${db:Status-Status}' clash-verge 2>/dev/null || true) == installed ]]; then
  printf 'Clash Verge Rev is already installed (%s).\n' "$(dpkg-query -W -f='${Version}' clash-verge)"
  exit 0
fi

version=2.5.10
arch=$(dpkg --print-architecture)
# SHA-256 digests from the upstream GitHub release assets for v2.5.10.
case $arch in
  amd64) sha256=add8564811ba4d2305da443288d23a26c5ed19674b676a0bb58b99ec4f7d13cf ;;
  arm64) sha256=729c3bdc84207984fd894cc0ff14e6451fc9335060b5c7efbe378a9f4dbdf8fd ;;
  armhf) sha256=346d9f2c621e093c0c63dc25234e88916923e7680ccaae887ce8b4acf73e5569 ;;
  *) printf 'Clash Verge Rev does not publish a supported .deb for %s.\n' "$arch" >&2; exit 1 ;;
esac

if [[ ${1-} == --check ]]; then
  printf 'Clash Verge Rev is not installed. Official %s package: v%s.\n' "$arch" "$version"
  exit 0
fi
if (( EUID == 0 )); then
  echo 'Run this installer as the regular Ubuntu desktop user.' >&2
  exit 1
fi
for command in python3 curl sudo; do
  if ! command -v "$command" >/dev/null; then
    printf '%s is required to install Clash Verge Rev.\n' "$command" >&2
    exit 1
  fi
done

setup_dir=$(dirname -- "$(realpath -- "$0")")
with_windows_proxy=(python3 "$setup_dir/ds-proxy-watch" --exec-windows)
sudo_for_apt=(sudo --preserve-env=http_proxy,https_proxy,no_proxy)
asset="Clash.Verge_${version}_${arch}.deb"
base='https://github.com/clash-verge-rev/clash-verge-rev/releases/download'
temp_dir=$(mktemp -d)
deb="$temp_dir/$asset"
trap 'rm -f -- "$deb"; rmdir -- "$temp_dir"' EXIT

printf 'Downloading official Clash Verge Rev v%s for %s...\n' "$version" "$arch"
downloaded=false
for attempt in 1 2 3; do
  if "${with_windows_proxy[@]}" curl -q --fail --location --silent --show-error \
      --proto '=https' --proto-redir '=https' --continue-at - --connect-timeout 15 --max-time 180 \
      --output "$deb" "$base/v$version/$asset"; then
    downloaded=true
    break
  fi
  if (( attempt < 3 )); then
    echo 'Download interrupted; retrying from the last received byte.' >&2
  fi
done
if ! $downloaded; then
  echo 'The upstream download did not complete. Retry with a working Windows proxy or direct connection.' >&2
  exit 1
fi
printf '%s  %s\n' "$sha256" "$deb" | sha256sum --check --status || {
  echo 'Clash Verge Rev download failed SHA-256 verification.' >&2
  exit 1
}

# APT's download user needs read access to this public package.
chmod 755 "$temp_dir"
chmod 644 "$deb"
"${with_windows_proxy[@]}" "${sudo_for_apt[@]}" apt-get update
"${with_windows_proxy[@]}" "${sudo_for_apt[@]}" apt-get install --yes --no-install-recommends "$deb"
echo 'Clash Verge Rev is installed. Open it from the Ubuntu application menu to configure your proxy.'
