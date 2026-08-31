# Precompiled Unity runtimes

## R3 1.3.1

R3 is pinned to official release `1.3.1` (`f6eed2dd4208dc4ae171c601e799e85f82aca25e`).
The Unity integration source is embedded at `Packages/com.cysharp.r3`; the following binaries
come from official NuGet packages and target Unity 2021.3's .NET Standard 2.1 profile:

- `R3.dll` 1.3.1: `4AC9EC66B56F4CBD139E4C7F99A6E0DF6E4BB4C2707635880DF8DD04ACE291EB`
- `Microsoft.Bcl.TimeProvider.dll` 8.0.0: `D9941B9603506C46E2AAF8B44166C93646B55EBEA0D365792B405C2562753442`
- `Microsoft.Bcl.AsyncInterfaces.dll` 6.0.0: `5705D245072D3EB78400547B32147DBB6E2C8B02BA8BDA76729798F5EFDEAECB`
- `System.Threading.Channels.dll` 8.0.0: `31C7E3704C0477C53D9306362DC6ABE741088EFB7A7B4E46CDED0169CF7BB0B2`

R3 and its Unity integration are MIT licensed. Model code does not depend on R3; Controllers keep
the mutable `ReactiveProperty` behind `IReadOnlyState<T>`, and Views receive only that read-only seam.

## MessagePipe 1.8.2

MessagePipe Core and its VContainer adapter are embedded from official release `1.8.2`
(`58516c36d4465a7b6396b7850a4ad7e03326998c`) under `Packages/`. Both are MIT licensed.
Business modules continue to depend on `IDomainEventBus`; MessagePipe remains an Infrastructure
implementation and is used only for discrete cross-module facts.

## Legacy protobuf runtime

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
