# Copy this file to vm-endpoint.local.psd1 and set the current guest endpoint.
# The local file is intentionally gitignored because Hyper-V guest addresses are
# machine-local and may change after VM/network maintenance.
@{
    # Examples: "admin@192.168.228.250" or "gamefactory-vm"
    Target = "admin@192.168.228.250"

    # Leave null to use the SSH default port/identity selection.
    Port = $null
    IdentityFile = $null
}
