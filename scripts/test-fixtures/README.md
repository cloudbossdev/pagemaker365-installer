# Test fixtures

`rfc3161-dotnet-runtime-signed-cms.b64` is a gzip+base64 transport encoding of
the `IndefiniteLengthContentDocument` CMS vector from
[`dotnet/runtime`](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Security.Cryptography.Pkcs/tests/SignedCms/SignedDocuments.cs).
It is used only to test the offline RFC 3161 verifier's positive and
message-imprint-forgery paths. The upstream source is licensed under the
[MIT License](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).
