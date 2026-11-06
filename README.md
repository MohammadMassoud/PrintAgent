# PrintAgent by SinergiaAI

Agente de impressão RAW para conectar aplicações .NET e Blazor a impressoras acessíveis por um computador Windows. Sua aplicação gera os comandos, o usuário escolhe a impressora e o agente envia os bytes pelo spooler do Windows ou por TCP.

**Um único pacote NuGet:** [`SinergiaAI.PrintAgent`](https://www.nuget.org/packages/SinergiaAI.PrintAgent). Inclui as bibliotecas Server, Client e Contracts. Licença [MIT](LICENSE), autoria de MohammadMassoud.

Pode enviar ZPL para Zebra, ESC/POS e outras linguagens RAW. O aplicativo escolhe o conteúdo adequado: o agente não detecta a linguagem, não converte PDF/imagens e não consegue garantir que uma impressora interpretou os comandos recebidos.

## Como funciona

```text
Tela do aplicativo → Backend do seu projeto → Fila SQLite
                                                  ↑
                                      Agente no Windows
                                                  ↓
                                      Spooler RAW / TCP
                                                  ↓
                                             Impressora
```

O agente consulta a API do seu projeto. A página não precisa abrir uma conexão JavaScript diretamente com a impressora ou com um servidor local. No Blazor WebAssembly, as chamadas devem passar pelo backend autenticado.

O site SinergiaAI hospeda o instalador. A API de impressão fica no backend do projeto que usará o agente.

## Requisitos

| Componente | Requisitos |
|---|---|
| Backend | .NET 10 e ASP.NET Core, HTTPS em produção e diretório persistente para dados |
| Computador das impressoras | Windows x64, permissão administrativa para instalar o serviço |
| Inicializador de instalação | .NET Framework 4.8, incluído no Windows 11 e em versões modernas do Windows 10 |
| Agente instalado | Inclui seu runtime .NET; não exige SDK no computador do cliente |

O pacote é destinado ao backend / Blazor Server. Não instale no projeto WebAssembly. Bibliotecas .NET sem SDK Web que consumam o pacote precisam declarar `<FrameworkReference Include="Microsoft.AspNetCore.App" />` no `.csproj`.

## 1. Instalar o pacote no projeto

```powershell
dotnet add package SinergiaAI.PrintAgent --version 0.2.0-preview.2
```

No Visual Studio, use **Gerenciar Pacotes NuGet → Procurar**, selecione **nuget.org** e marque **Incluir pré-lançamento** para localizar a versão preview.

Você instala somente esse pacote. SQLite e as demais dependências de terceiros são restaurados automaticamente. Os namespaces disponíveis são:

```csharp
using SinergiaAI.PrintAgent.Server;
using SinergiaAI.PrintAgent.Contracts;
using SinergiaAI.PrintAgent.Client;
```

Em uma solução com backend e biblioteca de páginas separados, adicione o mesmo pacote em cada projeto que utilizar os tipos.

## 2. Registrar a API no backend

Acrescente ao `Program.cs` do seu aplicativo ASP.NET Core, mantendo sua autenticação e autorização existentes:

```csharp
using Microsoft.AspNetCore.DataProtection;
using SinergiaAI.PrintAgent.Server;

// Antes de builder.Build():
var printingData = Path.Combine(
    builder.Environment.ContentRootPath, "App_Data", "PrintAgent");

builder.Services.AddSinergiaAIPrintAgentServer(options =>
{
    options.DatabasePath = Path.Combine(printingData, "printing.db");
});

// Se o host já configura Data Protection persistente, mantenha essa configuração.
// Caso contrário, configure um local persistente para as chaves:
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(printingData, "keys")));

// Depois de construir app e configurar os middlewares do seu projeto:
app.MapSinergiaAIPrintAgent("/printing");
```

Com esse prefixo, a URL fornecida ao agente será `https://seu-aplicativo.com/printing/`. Substitua pelo endereço público real do seu backend, incluindo eventual caminho base do aplicativo. Em desenvolvimento, HTTP é permitido somente para loopback, como `http://localhost:5189/printing/`.

Preserve o banco, as chaves de Data Protection e a identidade do aplicativo entre publicações. Em contêineres, use armazenamento persistente. Proteja o diretório contra acesso público e configure a proteção das chaves em repouso conforme seu ambiente.

`MapSinergiaAIPrintAgent` disponibiliza o protocolo do agente e o resgate da instalação. Seu aplicativo deve autorizar os usuários antes de criar instalações, baixar instaladores, listar impressoras, enviar trabalhos ou consultar resultados. O módulo não implementa usuários, propriedade das instalações ou isolamento entre empresas.

## 3. Criar a instalação e oferecer o download automático

No serviço do seu backend, injete `PrintStore` e `PrintEnrollmentService`. Depois de autorizar o usuário, crie uma instalação para o computador:

```csharp
var credentials = store.CreateInstallation("Computador da expedição");
```

Guarde o `InstallationId` e associe-o ao usuário/empresa na sua aplicação. Guarde o `AgentToken` criptografado no servidor: o módulo armazena apenas seu hash e não consegue recuperar o token original.

Crie uma instalação uma vez por vínculo com o computador; reabrir a tela ou ficar offline não deve criar outra instalação automaticamente.

Ao clicar em **Instalar agente**, seu endpoint autenticado verifica a propriedade da instalação, recupera as credenciais protegidas e devolve o EXE personalizado:

```csharp
// Trecho dentro do endpoint autorizado do seu aplicativo.
// credentials são as credenciais da instalação pertencente ao usuário atual.
var installer = enrollment.CreateInstaller(
    credentials,
    new Uri("https://seu-aplicativo.com/printing/"));

httpContext.Response.Headers.CacheControl = "no-store";
httpContext.Response.Headers.Pragma = "no-cache";

return Results.File(installer, "application/octet-stream",
    "SinergiaAI.PrintAgent-Instalar.exe");
```

Esse trecho exige a implementação, pelo seu aplicativo, do armazenamento protegido e da verificação de propriedade. Não exponha um endpoint público de criação ou download de instalações.

O fluxo do usuário será:

1. Clicar no botão de instalação na sua página.
2. Executar o arquivo baixado e autorizar a instalação no Windows.
3. Voltar à página; o agente se conecta ao backend e informa as impressoras.

O EXE personalizado contém a URL pública da API e um código aleatório de uso único, válido por 30 minutos. O inicializador resgata a configuração no backend, baixa o instalador genérico e verifica seu SHA-256. Não é necessário digitar URL, GUID ou token. Se o código expirar ou for consumido durante uma instalação cancelada, gere outro download.

O [instalador genérico](https://www.sinergiaai.com/downloads/SinergiaPrint-Setup.exe) também permite configuração manual. Para preencher automaticamente o contexto, use o download gerado pelo backend acima.

## 4. Mostrar conexão e impressoras na tela

Depois de verificar que o usuário tem acesso à instalação:

```csharp
var installation = store.GetInstallation(installationId);
var connected = installation?.LastSeenAt is { } lastSeen
    && DateTimeOffset.UtcNow - lastSeen < TimeSpan.FromSeconds(60);
var printers = installation?.Printers ?? Array.Empty<PrinterInfo>();
```

A janela de 60 segundos é um critério da interface, não uma garantia de disponibilidade. O módulo não detecta diretamente se o agente está instalado no Windows: o último contato indica que ele está se comunicando. Sem contato, ele pode estar ausente, parado ou sem acesso à API.

Na sua interface, mostre a conexão, ofereça a instalação quando necessário e preencha o seletor com `printers`, usando `Name` como texto e `Id` como valor. O primeiro relatório ocorre ao iniciar o agente; a lista é atualizada aproximadamente a cada 30 segundos. Consulte o backend periodicamente, por exemplo a cada 5 segundos.

As impressoras são aquelas visíveis ao serviço Windows. Filas instaladas somente no perfil de um usuário podem não aparecer para LocalSystem; verifique o contexto da instalação ou configure impressoras TCP nas opções do agente.

## 5. Enviar uma etiqueta ZPL e acompanhar o resultado

No backend, após validar a autorização, a instalação, o ID da impressora e os limites de conteúdo da sua aplicação:

```csharp
using System.Text;
using SinergiaAI.PrintAgent.Contracts;

var zpl = "^XA^FO30,30^A0N,30,30^FDTeste PrintAgent^FS^XZ";
var bytes = Encoding.ASCII.GetBytes(zpl);

// Crie uma chave para esta solicitação e preserve-a se houver retry.
var requestKey = Guid.NewGuid().ToString("N");

var job = store.Submit(new SubmitPrintRequest(
    installationId,
    selectedPrinterId,
    Convert.ToBase64String(bytes),
    requestKey));

// Guarde job.Id. Consulte novamente pelo backend para atualizar a tela:
var result = store.GetJob(job.Id);
```

Os trechos pressupõem `store` e `enrollment` injetados no serviço, além da instalação e impressora selecionadas pela aplicação. O módulo não inclui uma tela pronta.

`Submit` retorna quando o trabalho entra na fila. Consulte `GetJob` periodicamente até obter um estado final e mostre `Error` quando presente. Uma consulta imediatamente após o envio normalmente ainda retorna `Pending` ou `Claimed`.

O agente tenta enviar os bytes solicitados e reporta os erros encontrados. Não valida se ZPL é compatível com a impressora selecionada; um equipamento pode aceitar os bytes sem imprimir corretamente, sem produzir um erro detectável. A codificação e o conteúdo são responsabilidade da aplicação; ASCII é apenas o exemplo acima.

| Estado | Significado |
|---|---|
| `Pending` | Trabalho aguardando o agente. |
| `Claimed` | Trabalho reservado para uma tentativa. |
| `Sent` | Bytes enviados ao spooler/TCP; não confirma saída física do papel. |
| `Failed` | A tentativa retornou erro; pode ter ocorrido envio parcial. |
| `Unknown` | Houve interrupção ou falta de confirmação. |

Reutilize a mesma chave de idempotência ao repetir a mesma solicitação após uma falha de rede. Não gere uma chave nova em cada retry. Depois de `Failed` ou `Unknown`, confira a impressora antes de permitir uma nova cópia deliberada. O agente mantém um diário local e não reenvia automaticamente um trabalho que possa ter alcançado o equipamento.

## Alternativa: API hospedada separadamente

Se você utiliza o projeto `SinergiaAI.PrintAgent.Api` como serviço separado, registre o cliente HTTP no backend consumidor:

```csharp
using SinergiaAI.PrintAgent.Client;

builder.Services.AddSinergiaAIPrintAgentClient(
    new Uri(builder.Configuration["Printing:Url"]!),
    builder.Configuration["Printing:ApiKey"]!);
```

Injete `PrintingClient` e use `ListPrintersAsync`, `PrintAsync` e `GetJobAsync`. Preserve a chave de idempotência enviada a `PrintAsync`.

O cliente usa os endpoints de gerenciamento `/api/...` da API separada. Mapear somente `MapSinergiaAIPrintAgent` no aplicativo não cria esses endpoints; nesse caso, utilize `PrintStore` no seu backend, conforme o guia acima.

A chave `Printing:ApiKey` pertence ao backend. Não envie a chave ao navegador ou ao agente. Na API separada, configure uma chave aleatória com pelo menos 32 caracteres. Esta versão não oferece multitenancy: instâncias e bancos separados podem ser necessários conforme o isolamento dos seus projetos.

## Migração dos nomes antigos

Remova as referências aos pacotes antigos `SinergiaPrint.Server`, `SinergiaPrint.Client` e `SinergiaPrint.Contracts`, ou seus ProjectReferences locais, antes de adicionar o pacote único. Atualize também os imports e chamadas:

| Antes | Agora |
|---|---|
| `SinergiaPrint.Server` | `SinergiaAI.PrintAgent.Server` |
| `SinergiaPrint.Client` | `SinergiaAI.PrintAgent.Client` |
| `SinergiaPrint.Contracts` | `SinergiaAI.PrintAgent.Contracts` |
| `AddSinergiaPrintServer` | `AddSinergiaAIPrintAgentServer` |
| `MapSinergiaPrintAgent` | `MapSinergiaAIPrintAgent` |
| `AddSinergiaPrint` | `AddSinergiaAIPrintAgentClient` |

A solução é `SinergiaAI.PrintAgent.slnx`. Não combine as bibliotecas antigas com as novas.

Alguns identificadores operacionais permanecem por compatibilidade: serviço `SinergiaPrint Agent`, diretórios `%ProgramFiles%\SinergiaPrint` e `%ProgramData%\SinergiaPrint`, variáveis `SINERGIAPRINT_`, formato de inscrição e URL do instalador. Eles não são nomes de pacotes nem namespaces. Mantenha a URL de API já configurada nas instalações existentes, ou planeje sua migração.

## Desenvolvimento e distribuição

Os projetos internos ficam em `src/SinergiaAI.PrintAgent.*`: Server, Client e Contracts são incluídos no mesmo NuGet; Agent é o serviço Windows; Bootstrap é o inicializador embutido; Api é o host separado opcional. Não é necessário clonar este repositório para consumir o NuGet.

Para compilar e testar no Windows, com SDK .NET 10 e Python 3:

```powershell
dotnet build .\SinergiaAI.PrintAgent.slnx
python .\tests\integration.py
python .\tests\installer_validation.py
```

Os testes usam processos temporários e impressão TCP simulada; não instalam serviço nem imprimem fisicamente. O teste opcional `tests/enrollment.browser.cjs` exige Playwright/Chromium e a fixture do Comercial, que depende dos arquivos do piloto externo.

Para gerar somente o pacote NuGet:

```powershell
.\scripts\pack-nuget.ps1
python .\tests\package_integration.py
```

O `.nupkg` fica em `artifacts/nuget-unified/`. Veja [publicação e versionamento](docs/nuget-publication.md). O README exibido no NuGet é [o README do pacote](src/SinergiaAI.PrintAgent.Server/README.md), incluído durante o empacotamento. Alterar este repositório não atualiza automaticamente um pacote já publicado.

Para gerar os binários do serviço e, opcionalmente, o instalador Inno Setup:

```powershell
.\scripts\publish.ps1
.\scripts\publish.ps1 -BuildSetup
```

A segunda opção exige Inno Setup 6. Ela atualiza no Bootstrap o hash e a URL imutável do instalador; publique o arquivo correspondente no servidor de downloads antes de distribuir um NuGet que o referencie. Nunca inclua tokens ou configurações individuais no instalador público.

Os executáveis atuais não têm assinatura digital. Antes de produção, valide a instalação como serviço e a impressão em equipamento real, configure a retenção dos dados e preserve o diário local. Atualização automática do agente, conversão de documentos e painel administrativo não estão implementados nesta versão.
