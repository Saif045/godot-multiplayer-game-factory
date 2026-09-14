# GodotGAS Boundary

GodotGAS is the vendored GAS-domain implementation for abilities, attributes,
effects, and gameplay tags. GameFactory continues to own networking, authority,
replication, identity, and session lifecycle.

Netfox is separate and frozen while the GAS foundation is established. Do not
modify its code or put GAS state into rollback history without a later,
explicit cross-system task.

Keep direct GodotGAS GDScript interop inside `GodotGasAdapter`. C# gameplay
code should use that adapter rather than scattered string-based `Call` or
`Get` access to addon APIs.
