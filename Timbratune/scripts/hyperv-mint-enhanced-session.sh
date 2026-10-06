#!/usr/bin/env bash
# Run INSIDE a Linux Mint 22 / Ubuntu 24.04 Hyper-V guest (as your normal user; it asks for sudo).
# Sets up a Hyper-V "enhanced session" (RDP over hv_sock) so the VM gets sound, the PC's microphone
# and a shared clipboard. The host side needs (admin): Set-VMHost -EnableEnhancedSessionMode $true
# and Set-VM <name> -EnhancedSessionTransportType HvSocket.
# Afterwards: restart the VM, DON'T log in on the console, reconnect from Hyper-V Manager, and in
# "Show Options" → Local Resources → Remote audio → Settings choose "Play on this computer" and
# "Record from this computer".
set -euo pipefail

sudo apt-get update
sudo apt-get install -y xrdp pipewire-module-xrdp xfce4

# Only the first port= (the [Globals] listener) moves to Hyper-V's vsock. The session sections
# ([Xorg], [Xvnc]) keep port=-1; changing those too leaves a blue screen after login.
sudo sed -i -e '0,/^port=/s|^port=.*|port=vsock://-1:3389|' /etc/xrdp/xrdp.ini
sudo sed -i \
  -e 's|^security_layer=.*|security_layer=rdp|' \
  -e 's|^crypt_level=.*|crypt_level=none|' \
  -e 's|^bitmap_compression=.*|bitmap_compression=false|' \
  /etc/xrdp/xrdp.ini
sudo adduser xrdp ssl-cert

# Load the Hyper-V socket driver and keep VMware's vsock transport from grabbing vsock.
echo hv_sock | sudo tee /etc/modules-load.d/hv_sock.conf >/dev/null
echo "blacklist vmw_vsock_vmci_transport" | sudo tee /etc/modprobe.d/blacklist-vmw_vsock_vmci_transport.conf >/dev/null

# The remote session runs XFCE (reliable over xrdp; the console keeps Cinnamon) and must not reuse
# the console login's D-Bus session (PipeWire still needs the runtime dir), or it exits at once.
echo "xfce4-session" > "$HOME/.xsession"
grep -q "unset DBUS_SESSION_BUS_ADDRESS" /etc/xrdp/startwm.sh ||
  sudo sed -i '/^if test -r \/etc\/profile; then/i unset DBUS_SESSION_BUS_ADDRESS' /etc/xrdp/startwm.sh

sudo systemctl enable xrdp xrdp-sesman
echo
echo "Done. Restart the VM, don't log in on the console, then close the VM window and reconnect"
echo "from Hyper-V Manager: it offers a connection size and 'Show Options' (audio + microphone)."
