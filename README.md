# SinergiaPrint — versão inicial 0.2

Agente de impressão RAW para Windows, independente de SinergiaAI e de regras de pedidos, mesas ou produtos. O aplicativo prepara os bytes (ZPL, ESC/POS etc.), seleciona uma impressora e solicita o envio. O agente não valida a linguagem da impressora e não converte PDF ou imagens.

## Componentes

- `SinergiaPrint.Server`: módulo para hospedar a fila e os endpoints do agente dentro do backend de outro projeto.
- `SinergiaPrint.Bootstrap`: inicializador Windows pequeno, incorporado ao módulo Server, que recebe contexto de instalação e busca a configuração automaticamente.
- `SinergiaPrint.Api`: fila persistente em SQLite e comunicação autenticada.
- `SinergiaPrint.Agent`: serviço Windows; descobre impressoras, consulta a fila e envia os bytes por spooler RAW ou TCP.
- `SinergiaPrint.Client`: biblioteca C# com registro por injeção de dependências.
- `SinergiaPrint.Contracts`: contratos compartilhados.
- `samples/IntegrationDemo`: exemplo executável de integração C#.
- `scripts`: publicação e instalação como serviço.
- `installer`: instalador visual Inno Setup, com campos de URL, identificação e token. A geração do EXE depende de Inno Setup 6.

```text
Blazor Server / backend do aplicativo → API → SQLite
                                             ↑
                                       Agente Windows
                                             ↓
                                  Spooler RAW / TCP → Impressora
```

Esta versão usa uma chave de backend por implantação da API. Para projetos/clientes com isolamento independente, use instâncias separadas da API e bancos separados. Não há multitenancy ou autorização por usuário nesta versão. Seu backend deve autorizar os usuários antes de listar impressoras e enviar trabalhos.

## Testar sem impressora física

Requisitos: Windows x64, SDK .NET 10 e Python 3.

```powershell
dotnet build .\SinergiaPrint.slnx
python .\tests\integration.py
```

O teste inicia API e agente como processos temporários, usa portas locais livres e uma impressora TCP simulada. Não instala serviço, não muda impressoras e encerra os processos ao terminar. Bancos e logs ficam em `artifacts/test-*`.

Verificações: autenticação, isolamento de instalações, descoberta, bytes intactos, status de envio, idempotência, falha de conexão, persistência da fila, reserva concorrente, confirmação indevida por outra instalação, reinício com envio interrompido, expiração sem reimpressão, recuperação de confirmação e cliente C#.

## API de desenvolvimento

Uma chave aleatória local foi criada em `src/SinergiaPrint.Api/appsettings.Development.json`, ignorado pelo Git. Em um novo clone, configure `Printing__ApiKey` com pelo menos 32 caracteres aleatórios; não use uma chave de exemplo em produção.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project .\src\SinergiaPrint.Api --urls http://localhost:5189
```

Em outro terminal, leia a chave local e crie uma instalação:

```powershell
$config = Get-Content .\src\SinergiaPrint.Api\appsettings.Development.json -Raw | ConvertFrom-Json
$headers = @{ 'X-Api-Key' = $config.Printing.ApiKey }
$installation = Invoke-RestMethod -Method Post -Uri http://localhost:5189/api/installations -Headers $headers -ContentType application/json -Body '{"name":"Expedicao"}'
```

A resposta contém `installationId` e `agentToken`. Entregue ao computador somente o token daquela instalação; a chave de backend deve permanecer no servidor. O token é armazenado como hash na API.

Para executar o agente em primeiro plano, no terminal com as credenciais:

```powershell
$env:SINERGIAPRINT_Agent__ApiBaseUrl = 'http://localhost:5189'
$env:SINERGIAPRINT_Agent__InstallationId = $installation.installationId
$env:SINERGIAPRINT_Agent__Token = $installation.agentToken
$env:SINERGIAPRINT_Agent__DataPath = Join-Path $PWD 'artifacts\agent-dev'
dotnet run --project .\src\SinergiaPrint.Agent
```

Essas variáveis são usadas apenas nesse processo/terminal; não são configuração global do Windows.

Para uma impressora TCP opcional, acrescente antes de iniciar o agente:

```powershell
$env:SINERGIAPRINT_Agent__NetworkPrinters__0__Name = 'Zebra expedicao'
$env:SINERGIAPRINT_Agent__NetworkPrinters__0__Address = 'tcp://192.168.1.50:9100'
```

A API disponibiliza as impressoras depois do primeiro relatório do agente. Atualiza a lista a cada 30 segundos. Os IDs são locais à instalação e precisam ser reencontrados se uma impressora mudar de nome/endereço.

## Integração simples com Blazor

Para instalação automática, o backend autorizado chama `PrintEnrollmentService.CreateInstaller(credentials, apiBaseUrl)` e devolve os bytes como download privado de um `.exe`. O código temporário é aleatório, tem validade de 30 minutos e uso único. A configuração com o token permanente fica criptografada no servidor; o arquivo inclui somente a URL da API e o código. O inicializador resgata por `POST /sinergiaprint/enrollment/redeem`, baixa a versão imutável do pacote e confere seu SHA-256 antes de iniciar o instalador com a configuração pronta. Não é necessário preencher URL/GUID/token.

O inicializador exige .NET Framework 4.8, incluído no Windows 11 e Windows 10 moderno (1903+). O agente propriamente dito continua self-contained em .NET 10. A configuração temporária fica em pasta privada e é removida depois da instalação. O download autenticado e o resgate usam `Cache-Control: no-store`. Preserve as chaves de Data Protection e o banco da fila entre reinícios/publicações. Se um código expirar ou for consumido durante uma instalação cancelada, gere um novo download.

Para hospedar a API no próprio projeto, referencie `SinergiaPrint.Server`, registre `builder.Services.AddSinergiaPrintServer(...)` com um caminho persistente para o banco e mapeie `app.MapSinergiaPrintAgent("/sinergiaprint")`. A URL do instalador será o endereço desse projeto seguido de `/sinergiaprint/`. Os endpoints do agente autenticam o token da instalação e usam opções JSON próprias, sem alterar a serialização do aplicativo hospedeiro. O backend é responsável por autorizar usuários ao criar vínculos e enviar trabalhos pelo `PrintStore`. O piloto do Comercial implementa esse adaptador.

A integração com `SinergiaPrint.Client` abaixo é a alternativa para quem usa a API como serviço separado.

Os pacotes `SinergiaPrint.Server`, `SinergiaPrint.Client` e `SinergiaPrint.Contracts` estão publicados no NuGet.org na versão `0.2.0-preview.1`. Para integrar a API ao próprio backend, instale `SinergiaPrint.Server`. Para comunicar com uma API separada, instale `SinergiaPrint.Client`. `Contracts` é instalado como dependência.

```powershell
dotnet add package SinergiaPrint.Server --version 0.2.0-preview.1
```

Também é possível manter referências locais aos projetos durante o desenvolvimento. A licença dos pacotes ainda não foi declarada.

```csharp
using SinergiaPrint.Client;

builder.Services.AddSinergiaPrint(
    new Uri(builder.Configuration["Printing:Url"]!),
    builder.Configuration["Printing:ApiKey"]!);
```

Injete `PrintingClient` no serviço/tela de Blazor Server:

```csharp
var printers = await printing.ListPrintersAsync(installationId);

// O seletor da tela fornece selectedPrinterId.
// Preserve esta chave se repetir a mesma solicitação após uma falha de rede.
var requestKey = Guid.NewGuid().ToString();
var bytes = System.Text.Encoding.ASCII.GetBytes(zpl);
var job = await printing.PrintAsync(
    installationId, selectedPrinterId, bytes, requestKey);

var result = await printing.GetJobAsync(job.Id);
```

A codificação pertence ao aplicativo que gera os comandos. ASCII é apenas um exemplo; o agente não altera os bytes.

`PrintAsync` retorna ao enfileirar. Consulte `GetJobAsync` com intervalo moderado até um estado final; após timeout de tela, preserve o ID para consulta posterior. Não crie outro trabalho automaticamente.

**Blazor WebAssembly:** use um endpoint autenticado do seu backend como intermediário e registre o cliente C# nele. Nunca coloque a chave da API no navegador. Nenhum JavaScript de impressão é necessário neste modelo.

## Estados e garantias

| Estado | Significado |
|---|---|
| Pending | Aguardando agente. |
| Claimed | Reservado para uma tentativa. |
| Sent | Bytes aceitos pelo spooler ou enviados pelo TCP. Não confirma saída física do papel. |
| Failed | A tentativa retornou erro; confira a impressão antes de solicitar outra. Pode ter ocorrido envio parcial. |
| Unknown | Houve interrupção ou falta de confirmação. Confira fisicamente antes de reenviar. |

- Uma chave de idempotência evita duplicar a solicitação. Reutilizar a mesma chave com destino/conteúdo diferentes retorna 409.
- Reserva atômica; cada instalação recebe um trabalho por vez.
- O agente registra localmente antes de enviar e grava o resultado antes de confirmar à API.
- Após falha de comunicação, repete a **confirmação**, sem repetir o envio à impressora.
- Trabalho reservado sem confirmação por dois minutos passa para Unknown ao consultar o resultado ou buscar trabalho. Nunca volta automaticamente para Pending.
- Reinício com envio em andamento resulta em Unknown. Não é possível garantir exatamente uma impressão física com spooler/TCP sem confirmação do equipamento.
- Outra impressão deliberada exige uma nova chave. A interface deve deixar claro que pode produzir uma cópia.

## Pacote e serviço Windows

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

Gera `artifacts/SinergiaPrint-Agent-win-x64.zip`, com runtime .NET incluído. Extraia no computador de destino e execute `instalar.bat` **como administrador**. Informe URL da API, ID da instalação e token quando solicitado. Não precisa instalar o SDK/runtime .NET no computador de destino.

O instalador configura `SinergiaPrint Agent` com início automático atrasado e reinício após falha. Binários em `%ProgramFiles%\SinergiaPrint`; configuração e diário em `%ProgramData%\SinergiaPrint`, acessíveis apenas a Administradores e LocalSystem. O serviço é independente de `SinergiaAI Print Agent`.

No ZIP para instalação por script, é possível incluir `appsettings.Local.json` na raiz de um pacote individual já configurado:

```json
{
  "Agent": {
    "ApiBaseUrl": "https://impressao.seudominio.com/",
    "InstallationId": "GUID-DA-INSTALACAO",
    "Token": "TOKEN-DA-INSTALACAO",
    "NetworkPrinters": [
      { "Name": "Zebra expedicao", "Address": "tcp://192.168.1.50:9100" }
    ]
  }
}
```

Esse arquivo contém credenciais; distribua individualmente. Sem arquivo novo, uma atualização reutiliza a configuração existente. O diário persistente é mantido nas atualizações e desinstalações.

Para gerar o instalador EXE, instale Inno Setup 6, disponibilize `ISCC.exe` no PATH e execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -BuildSetup
```

O instalador EXE genérico foi compilado e aceita configuração automática por `/CONFIGFILE`, usada pelo inicializador. Na execução manual, conserva o assistente em português para informar URL/ID/token. Em atualizações manuais, permite manter a conexão existente. O pacote público exclui configurações pessoais e a publicação recusa `appsettings.Local.json` na pasta do agente. Os dados são validados antes de alterar o serviço, e falhas na instalação são reportadas.

Testes de validação sem instalar serviço: `python tests/installer_validation.py`. A instalação/atualização/desinstalação completa como serviço ainda precisa ser validada em uma máquina de teste. Os executáveis não têm assinatura digital. A instalação automática por contexto está implementada no piloto do Comercial; a instalação manual continua disponível.

`publish.ps1 -BuildSetup` atualiza o hash e o endereço imutável do pacote no inicializador e recompila o módulo Server em Release. Publique a versão do arquivo correspondente e recompile os projetos consumidores antes de distribuir. O servidor de downloads conserva os artefatos por hash, para que um projeto com versão anterior do módulo continue conseguindo instalar sua versão correspondente.

Teste integrado da instalação automática: compile `tests/ComercialFixture/Fixture.csproj` e execute `node tests/enrollment.browser.cjs`, com Playwright e Chromium disponíveis (`PLAYWRIGHT_MODULE` e `CHROME_PATH` podem sobrescrever seus caminhos). A fixture usa os arquivos reais da página/serviço do Comercial, banco e usuário artificiais, e a impressora TCP simulada. Ela não inicia os serviços gerais do ERP nem instala um serviço Windows. Os modos de diagnóstico `--resolve-only` e `--download-only` do inicializador permitem validar o resgate e o download sem executar a instalação elevada.

Download público do instalador genérico: [SinergiaPrint-Setup.exe](https://www.sinergiaai.com/downloads/SinergiaPrint-Setup.exe). O site hospeda somente o arquivo; não define a API do projeto. A API configurada deve oferecer os endpoints deste projeto: um backend existente do ERP, sem esse módulo, ainda não é suficiente.

## Operação e próximos testes

- Hospede a API com HTTPS; HTTP é aceito pelo agente somente para loopback em desenvolvimento. O backend deve configurar autenticação e autorização dos usuários.
- Impressoras instaladas somente no perfil de um usuário podem não aparecer para LocalSystem. Instale/verifique as filas no contexto do serviço ou configure TCP.
- Esta versão tem polling de 2 segundos (configurável) e SQLite; é adequada para validar a integração. Não há painel, retenção automática, rotação de credenciais ou atualização automática do agente.
- Os registros e bytes de impressão ficam persistidos; definir política de retenção antes de produção.
- Imprimir PDF/imagens exige renderização específica futura. RAW não converte documentos.
- Testar uma etiqueta em Zebra real (dimensão, DPI e linguagem) e instalar o serviço em VM antes do piloto com um usuário.
- O SinergiaAI e os demais projetos existentes não foram modificados.
