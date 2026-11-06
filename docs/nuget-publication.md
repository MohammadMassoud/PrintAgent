# Publicação do pacote único

Produto: **PrintAgent**, da SinergiaAI. Pacote: **SinergiaAI.PrintAgent**.
Versão preparada: **0.2.0-preview.2**. Requer **.NET 10 e ASP.NET Core**.
Licença MIT, autorizada pelo proprietário. Repositório: https://github.com/MohammadMassoud/PrintAgent.

## O que publicar

Somente `artifacts/nuget-unified/SinergiaAI.PrintAgent.0.2.0-preview.2.nupkg`.

O arquivo contém `SinergiaAI.PrintAgent.Server.dll`, `SinergiaAI.PrintAgent.Client.dll` e `SinergiaAI.PrintAgent.Contracts.dll`, além do README e da licença. O inicializador Windows está embutido em Server. Não há dependência dos antigos pacotes SinergiaPrint. Dependências de terceiros, como SQLite, são restauradas pelo NuGet.

Os projetos internos continuam separados para manutenção, mas apenas Server gera um pacote, usando o ID público SinergiaAI.PrintAgent. O EXE de instalação do serviço Windows continua separado: instalar o NuGet no backend não instala um serviço na máquina do usuário. O inicializador embutido baixa o instalador público e valida seu SHA-256.

## Gerar e verificar

No PowerShell, na pasta do repositório:

```powershell
dotnet build .\SinergiaAI.PrintAgent.slnx
python .\tests\integration.py
.\scripts\pack-nuget.ps1
python .\tests\package_integration.py
```

`pack-nuget.ps1` apenas empacota as bibliotecas; não publica no NuGet e não gera nem publica outro instalador. O teste verifica as DLLs, os metadados e a ausência de dependências antigas, restaura o pacote em um projeto isolado e executa SQLite, criação do instalador, resgate de uso único e registro do cliente/endpoints. Não instala serviço nem imprime em impressora física.

## Publicar manualmente

1. Entre em https://www.nuget.org/packages/manage/upload com a conta proprietária.
2. Escolha o único `.nupkg` em `artifacts/nuget-unified/` correspondente à versão atual.
3. Confira ID `SinergiaAI.PrintAgent`, versão, licença MIT, repositório e README. Não escolha os arquivos antigos em `artifacts/nuget/`.
4. Clique em **Submit** e aguarde a validação/indexação do NuGet.

Não é necessária chave de API para o upload pelo site. A disponibilidade do novo ID será confirmada pelo próprio NuGet no upload. A criação do arquivo local não significa que ele já esteja publicado.

Nas próximas versões, atualize `Version` em `build/NuGetPackage.props`, as instruções de instalação e as notas de versão antes de gerar outro arquivo. Não reutilize uma versão já publicada.

## Instalar ou migrar

Após a publicação:

```powershell
dotnet add package SinergiaAI.PrintAgent --version 0.2.0-preview.2
```

Remova antes os PackageReferences antigos `SinergiaPrint.Server`, `SinergiaPrint.Client` e `SinergiaPrint.Contracts`, ou os ProjectReferences equivalentes. Instale o pacote único em cada projeto que utilizar os tipos; em uma solução com backend e biblioteca de páginas separados, ambos podem precisar da referência. Não combine os pacotes antigos com o novo.

Atualize os namespaces para `SinergiaAI.PrintAgent.Server`, `SinergiaAI.PrintAgent.Client` e `SinergiaAI.PrintAgent.Contracts`. As extensões agora são `AddSinergiaAIPrintAgentServer`, `MapSinergiaAIPrintAgent` e `AddSinergiaAIPrintAgentClient`. As DLLs e os projetos também seguem esse padrão. A solução agora é `SinergiaAI.PrintAgent.slnx`.

Projetos sem SDK Web precisam declarar `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. O pacote unificado é destinado ao backend / Blazor Server, não ao WebAssembly. Veja o README do pacote em `src/SinergiaAI.PrintAgent.Server/README.md` para registro e endpoints.

Para desenvolvimento com ProjectReference ao Server, referencie Contracts explicitamente nos projetos que usam seus tipos. As referências internas ficam privadas para evitar dependências de pacotes separados durante o empacotamento.

## Histórico

Os três pacotes `SinergiaPrint.Contracts`, `SinergiaPrint.Client` e `SinergiaPrint.Server` foram publicados anteriormente na versão `0.2.0-preview.1`. Em 06/11/2026, os três foram retirados da listagem (unlisted) a pedido do proprietário. O NuGet não permite exclusão permanente pelo painel. A restauração por versão exata continua disponível; as buscas podem demorar algumas horas para refletir a retirada.

O piloto do Comercial ainda usa referências locais; seus caminhos, imports e chamadas foram atualizados para os nomes novos. A troca para PackageReference pode ser feita depois da publicação.

Identificadores de compatibilidade do serviço Windows, diretórios de dados, formato de inscrição e URL pública do instalador foram preservados para não desconectar instalações existentes. Isso não afeta os novos namespaces e nomes das DLLs.
