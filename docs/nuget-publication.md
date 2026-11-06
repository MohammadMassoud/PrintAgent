# Primeira publicação no NuGet

Publicação confirmada em 06/11/2026 na conta `MohammadMassoud`. Os três pacotes aparecem em **Published Packages** depois da validação automática.

Versão: `0.2.0-preview.1`. Destino: .NET 10.

- [SinergiaPrint.Contracts](https://www.nuget.org/packages/SinergiaPrint.Contracts/0.2.0-preview.1)
- [SinergiaPrint.Client](https://www.nuget.org/packages/SinergiaPrint.Client/0.2.0-preview.1)
- [SinergiaPrint.Server](https://www.nuget.org/packages/SinergiaPrint.Server/0.2.0-preview.1)

O pacote Server inclui o inicializador Windows como recurso embutido. Não depende de um pacote Bootstrap separado nem de caminhos locais no projeto consumidor. O Client utiliza Microsoft.Extensions.Http e funciona também em um console .NET, sem exigir referência ao framework ASP.NET Core.

Os três `.nupkg` foram inspecionados e consumidos em projetos isolados, sem ProjectReference ao código: SQLite, registro de endpoints, inicializador EXE, resgate de uso único e injeção de dependências do cliente passaram.

Metadados: autor `MohammadMassoud`, README por pacote, versão prévia e licença ainda não declarada. Nenhuma licença de terceiros foi escolhida em nome do proprietário. Uma atualização de metadados/conteúdo deve usar nova versão; não reutilizar esta versão publicada.

Para instalar no backend:

```powershell
dotnet add package SinergiaPrint.Server --version 0.2.0-preview.1
```

O piloto do Comercial ainda utiliza referências locais. A migração dele para PackageReference não fez parte da publicação.

Artefatos locais em `artifacts/nuget/`; comprovante visual em `artifacts/nuget/nuget-publication.png`.
