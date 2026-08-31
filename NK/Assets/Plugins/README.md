# Legacy protobuf runtime

`protobuf-net.dll` is version `2.4.4.9` from the audited Unity client at
`E:\ClientProject\Assets\Plugins\protobuf-net.dll`.

- SHA-256: `141B235AF8EBF25D5841EDEE29E2DCF6297B8292A869B3966C282DA960CBD14D`
- Scope: `Game.Infrastructure.Network` only
- Purpose: byte-compatible serialization for the frozen LegacyNetworkV1 transport
- License: Apache-2.0 (protobuf-net upstream)
- Provenance: SHA-256 exactly matches the official NuGet 2.4.4 `lib/net40/protobuf-net.dll`

Business Model, Controller, View, and Boot code must not reference `ProtoBuf` types.
The dependency remains isolated until the transport layer is explicitly unfrozen.
Version 2.4.4 is legacy compatibility debt: it must not be referenced by new modules even though
the upstream repository had no published security advisories at the 2026-08-31 audit.
