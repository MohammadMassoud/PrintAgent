# PrintAgent: funcionamento técnico e integração

Este guia descreve o serviço Windows 0.3.0, o NuGet `SinergiaAI.PrintAgent` 0.3.0-preview.1 e a integração Blazor Server utilizada no Comercial. São componentes diferentes: instalar o NuGet no servidor não instala o serviço no computador do usuário.

## Componentes e responsabilidades

| Componente | Onde executa | Responsabilidade |
|---|---|---|
| Página Blazor | Interface no navegador; lógica C# no servidor | Selecionar impressora, gerar conteúdo e apresentar resultados |
| Módulo JavaScript | Navegador do usuário | Consultar o agente local e baixar o instalador contextualizado |
| Integração compartilhada | Backend do aplicativo | Autorizar operações, reconhecer a máquina e enfileirar documentos |
| `PrintStore` | Backend | Instalações, impressoras e fila persistente SQLite |
| `PrintDiscoveryService` | Backend | Desafios temporários e comprovantes da instalação |
| `PrintEnrollmentService` | Backend | Gerar EXE contextualizado e resgatar configuração de uso único |
| Bootstrap | Computador do usuário, durante a instalação | Recuperar contexto e baixar/verificar o instalador genérico |
| `AgentWorker` | Serviço Windows | Informar impressoras, buscar trabalhos, enviar bytes e confirmar resultados |
| `LocalAgentBridge` | Mesmo serviço Windows | Permitir descoberta somente pelo próprio computador |
| `AgentJournal` | Computador do usuário | Registrar tentativas e evitar reenvio automático após entrega incerta |

O pacote único inclui as DLLs Server, Client e Contracts. `IAgenteImpressaoService`, `IAgenteImpressaoConexao` e `AddAgenteImpressao` pertencem à biblioteca compartilhada **Sinergia.Maui.Common**, do projeto consumidor. Não são APIs distribuídas dentro do NuGet público.

## O que acontece ao baixar e executar

1. O usuário entra no aplicativo e a tela tenta reconhecer o agente local. Abrir a tela ou falhar a conexão não cria uma instalação nova automaticamente.
2. Ao clicar em instalar, a integração tenta reconhecer novamente a máquina. Se existe um vínculo válido salvo, ele é reutilizado. Uma instalação nova é criada apenas quando necessária para o download solicitado.
3. O backend gera um pequeno EXE contextualizado. Ele contém o endereço público da API e um código aleatório, de uso único e validade de 30 minutos. O token permanente não está embutido nesse EXE.
4. **O download sozinho não instala nada.** Ao executar o arquivo, o Bootstrap resgata a configuração em `/enrollment/redeem`.
5. Ele baixa o instalador genérico de uma URL imutável, verifica o SHA-256 fixado na versão do pacote e passa a configuração por um arquivo temporário privado. O usuário não precisa digitar URL, GUID ou token.
6. O instalador solicita autorização administrativa, valida a configuração e para a versão anterior do serviço antes de substituir os arquivos.
7. Os executáveis são instalados em `%ProgramFiles%\SinergiaPrint`. A configuração e o diário ficam em `%ProgramData%\SinergiaPrint`, com acesso restrito a Administradores e LocalSystem.
8. É criado ou atualizado o serviço **SinergiaPrint Agent**, com início automático atrasado e tentativas de reinício em caso de falha. Ele é iniciado ao concluir a instalação.
9. O usuário volta à página e reconhece o computador. O serviço informa as impressoras à API; a tela pode listá-las e enviar trabalhos.

O Bootstrap usa .NET Framework 4.8. O agente Windows x64 inclui seu runtime .NET. Não é necessário Visual Studio nem SDK no cliente. Os executáveis atuais não possuem assinatura digital. Se o código de instalação expirar ou já tiver sido usado, baixe um novo EXE pela página.

## Como o serviço encontra a API

O serviço lê `%ProgramData%\SinergiaPrint\appsettings.Local.json`. Os principais valores são:

```json
{
  "Agent": {
    "ApiBaseUrl": "https://seu-aplicativo.com/printing/",
    "InstallationId": "00000000-0000-0000-0000-000000000000",
    "Token": "PREENCHIDO_PELO_INSTALADOR",
    "LocalBridgePort": 17831,
    "PollSeconds": 2,
    "NetworkPrinters": []
  }
}
```

Este é um exemplo explicativo, não uma configuração utilizável: o GUID precisa ser válido e o token deve ser emitido pelo backend. Não publique o arquivo real. O servidor mantém o hash do token para autenticar; quando precisa recuperar credenciais para reinstalação, a descoberta as armazena protegidas com Data Protection.

O agente faz requisições de saída para sua API usando `X-Agent-Token`. Ele consulta a fila aproximadamente a cada dois segundos quando ocioso; havendo trabalhos, continua processando sem esperar esse intervalo entre cada item. Informa as impressoras ao iniciar e aproximadamente a cada 30 segundos. Não depende de uma sessão de usuário do Comercial aberta.

O site SinergiaAI hospeda apenas o instalador. A API e a fila pertencem ao aplicativo consumidor. A configuração atual aceita **uma API por agente instalado**; conectar vários sistemas simultaneamente exigiria suporte adicional.

## Como o navegador identifica o próprio computador

O serviço mantém uma ponte em `http://127.0.0.1:17831`, somente na interface de loopback. Cada computador acessa seu próprio endereço; a ponte não fica disponível na rede LAN. A porta é configurável nos dois lados e não pode estar ocupada por outro programa.

```text
Navegador → agente local: GET /info
Navegador → backend autenticado: obter desafio
Navegador → agente local: POST /connect (URL da API + desafio)
Agente → sua API: POST /machine/confirm (desafio + token)
Navegador → backend autenticado: resgatar comprovante
```

O desafio expira em dois minutos. O backend valida a credencial da instalação e emite um comprovante opaco, próprio daquele navegador, para a mesma instalação. O navegador não recebe o token permanente. O ID público de instalação sozinho não autoriza operações.

O vínculo é com a instalação do serviço, não com o login, IP, nome da máquina ou `localStorage`. O armazenamento do navegador apenas guarda um comprovante para reutilização. Outro navegador ou perfil pode reconhecer o mesmo serviço e obter seu próprio comprovante. O usuário ainda precisa estar autenticado e autorizado pelo aplicativo; isso não implementa isolamento entre empresas automaticamente.

No Blazor Server, o C# da página executa no servidor. Consultar `localhost` em C# acessaria esse servidor. Por isso o JavaScript é necessário para consultar o computador do usuário. A impressão continua passando pela fila do backend; a ponte não recebe comandos de impressão.

O navegador pode solicitar permissão de acesso local em cada perfil. A ponte verifica a origem do aplicativo, a URL exata da API e a origem loopback da conexão. Em desenvolvimento, permite origens loopback HTTP/HTTPS em portas distintas. Em produção, a API utiliza HTTPS.

## Conexão e atualização são verificações diferentes

| Situação observada | Comportamento |
|---|---|
| Prova local válida e contato recente com a API | Conectado; listar impressoras e permitir envio |
| Prova local válida, sem contato recente do worker | Aguardar/verificar comunicação com a API |
| `fetch` local falhou | Informar indisponibilidade e orientar sobre serviço e permissão; não afirmar que está desinstalado |
| Ponte respondeu HTTP 403 | Informar origem não autorizada |
| Ponte informou outra API | Informar configuração para outro aplicativo |
| Produto/protocolo incompatível | Informar incompatibilidade do serviço local |
| Versão instalada conhecida inferior à versão distribuída | Oferecer atualização |

Uma falha de `fetch` pode representar programa ausente, serviço parado, porta ocupada ou bloqueio do navegador. Não é possível distinguir todos esses casos apenas pela falha de rede. Um agente 0.2 sem ponte local também não pode ser identificado nem ter sua versão inferida por essa consulta.

O JavaScript retorna a versão de `/info`; a comparação ocorre em C# na integração compartilhada. Versão ausente, inválida, igual ou superior não gera aviso de atualização. Atualmente o executável é **0.3.0**, enquanto o pacote de bibliotecas é **0.3.0-preview.1**; não se comparam esses números entre si.

`AgenteImpressaoOptions.VersaoAgenteDisponivel` deve corresponder ao instalador realmente fixado no Bootstrap do pacote utilizado, atualmente `0.3.0`. Ela não consulta a versão mais recente na internet. Não aumente esse valor sem disponibilizar o instalador correspondente. A atualização é manual: o usuário baixa, executa e autoriza o instalador. Atualizar o NuGet ou o README não atualiza o serviço Windows.

## Integração no Comercial

Arquivos compartilhados, relativos à raiz da solução do Comercial:

```text
SharedTi/Sinergia.Maui.Common/
  Interfaces/IAgenteImpressaoService.cs
  Interfaces/IAgenteImpressaoConexao.cs
  Services/Printing/AgenteImpressaoService.cs
  Services/Printing/AgenteImpressaoConexao.cs
  Services/Printing/AgenteImpressaoEndpoints.cs
  Services/Printing/AgenteImpressaoOptions.cs
  Services/Printing/print-agent.js
```

`IAgenteImpressaoConexao` encapsula importação do JavaScript, leitura/gravação do comprovante, migração antiga, descoberta, versão e download. `IAgenteImpressaoService` executa as operações de backend e autoriza o acesso à instalação. Ambos são registrados como scoped pelo registro abaixo e compartilham o contexto do circuito Blazor.

```csharp
using Sinergia.Maui.Common.Services;

builder.Services.AddAgenteImpressao(options =>
{
    options.ApiPrefix = "/printing";
    options.DataDirectory = "App_Data/PrintAgent";
    options.PublicBaseUrl = "https://seu-aplicativo.com/printing/";
    options.LocalBridgePort = 17831;
    options.VersaoAgenteDisponivel = "0.3.0";
});

// Após a configuração dos middlewares de autenticação/autorização:
app.MapAgenteImpressao();
```

No Comercial existente, o prefixo `/sinergiaprint` e os identificadores antigos de armazenamento/proteção foram mantidos por compatibilidade. Preserve a URL já configurada nos agentes. A exceção de redirecionamento HTTPS é restrita ao desenvolvimento e às chamadas loopback do prefixo do agente; produção continua utilizando HTTPS. O middleware que transforma erros em páginas HTML não deve interceptar as respostas dessa API.

As rotas do protocolo do agente vêm do NuGet. O wrapper compartilhado acrescenta `browser.js`, `machine/challenge`, `machine/proof` e `installers/{id}`. As rotas de desafio, comprovante e download exigem login; o download também exige o comprovante em `X-PrintAgent-Machine`, nunca na URL. Rotas do agente usam sua própria autenticação por token, em vez do cookie do usuário.

### Uso em qualquer página Blazor Server

```razor
@using Sinergia.Maui.Common.Interfaces
@inject IAgenteImpressaoConexao ConexaoAgente
@inject IAgenteImpressaoService Impressao
@inject NavigationManager Navigation
```

Na lógica da página, após a primeira renderização interativa:

```csharp
private ResultadoConexaoAgente? _agente;

protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (!firstRender) return;
    _agente = await ConexaoAgente.VerificarConexaoAsync(new Uri(Navigation.BaseUri));
    // _agente.Conectado: habilitar impressão.
    // _agente.Conexao?.Instalacao.Printers: preencher o seletor.
    // _agente.Estado / Mensagem: explicar falhas.
    // _agente.AtualizacaoDisponivel: oferecer atualização.
    StateHasChanged();
}

private async Task InstalarAsync()
{
    await ConexaoAgente.BaixarInstaladorAsync(new Uri(Navigation.BaseUri));
    // O usuário executa o EXE; depois consulte VerificarConexaoAsync novamente.
}
```

A página não importa o módulo JS nem manipula o comprovante para conectar. Trate exceções na interface e use um CancellationToken ligado ao ciclo de vida da página. A consulta inicial deve ocorrer em uma renderização interativa, não em prerenderização. Se atualizar o estado periodicamente, cancele o timer ao sair e evite consultas sobrepostas.

Esses serviços Common não existem automaticamente em outro projeto apenas por instalar o NuGet. Em outro sistema, referencie uma biblioteca compartilhada compatível ou implemente seu adaptador de autenticação e descoberta com `PrintDiscoveryService`. Não copie regras de produtos/pedidos para o agente.

## Do clique em imprimir até a impressora

1. A aplicação gera bytes ZPL, ESC/POS ou outra linguagem RAW.
2. Seleciona o ID da impressora reportada pelo agente e atribui uma chave estável à solicitação.
3. `EnviarAsync` ou `EnviarLoteAsync` valida acesso, impressora e limites; o backend grava os trabalhos na fila SQLite.
4. O worker reserva um trabalho por vez e grava o início no diário local **antes** de tentar enviar.
5. `WindowsRawReceiptPrinter` encaminha os bytes ao spooler RAW ou ao destino TCP configurado.
6. O worker registra o resultado no diário e confirma na API. Se a confirmação falhar, repete a confirmação, não a impressão.
7. A página consulta os trabalhos para apresentar `Pending`, `Claimed`, `Sent`, `Failed` ou `Unknown` e o erro disponível.

```csharp
// Depois de reconhecer a máquina, com _agente.Conectado == true:
var installationId = _agente!.Conexao!.Instalacao.Id;
var documento = new DocumentoAgenteImpressao(
    impressoraId,
    System.Text.Encoding.ASCII.GetBytes("^XA^FO30,30^A0N,30,30^FDTeste^FS^XZ"),
    chaveDaSolicitacao); // Gere uma vez e preserve antes de tentar enviar.
var trabalho = await Impressao.EnviarAsync(installationId, documento);
var resultados = await Impressao.ObterTrabalhosAsync(installationId, new[] { trabalho.Id });
```

Os nomes `impressoraId` e `chaveDaSolicitacao` representam valores da aplicação. Uma consulta imediata normalmente ainda retorna Pending/Claimed; consulte novamente até o estado final.

O wrapper aceita de 1 a 5000 documentos por lote, até 2 MiB por documento e chaves únicas de até 200 caracteres. Cada documento vira um trabalho. O lote é enfileirado na ordem recebida, mas não constitui uma transação única; solicitações concorrentes de outras pessoas podem se intercalar. Não é garantia de velocidade física nem de reserva exclusiva da impressora para o lote inteiro.

Em interrupção parcial, reutilize as mesmas chaves e conteúdo para recuperar os mesmos trabalhos. Não gere novas chaves em tentativas de recuperar uma solicitação. O agente não reenvia automaticamente um trabalho que possa ter alcançado a impressora. `Sent` significa bytes encaminhados, não confirmação física de papel. `Failed` também pode ter envio parcial; confira o equipamento antes de solicitar outra cópia.

O agente não valida linguagem, não converte documentos e não garante que ZPL será interpretado por uma impressora escolhida incorretamente. A geração de código, descrição, sequência e código de barras pertence à aplicação. Impressoras instaladas apenas para um usuário Windows podem não estar disponíveis ao serviço LocalSystem.

## Remover vínculo, desinstalar e manter

`IAgenteImpressaoConexao.RemoverVinculoAsync` revoga o comprovante deste navegador e remove sua referência local. Não cancela trabalhos, não apaga a instalação e não revoga o token do serviço. Outros navegadores continuam funcionando. Enquanto instalado, o agente poderá ser reconhecido novamente.

Para remover o programa, use Aplicativos instalados do Windows, selecione **SinergiaPrint Agent** e desinstale com permissão administrativa. A página oferece um atalho para essa configuração, sem executar desinstalação silenciosa. Preserve dados necessários para auditoria/diagnóstico e planeje retenção do banco e diário.

Persistir SQLite, as chaves de Data Protection e a identidade da aplicação é necessário para sobreviver a publicações. Não hospede dados/configurações no diretório público. A versão atual utiliza SQLite; múltiplas instâncias do backend e isolamento entre empresas precisam de planejamento próprio.

Para diagnosticar: verifique o serviço em `services.msc`, a permissão local no navegador, a disponibilidade da API configurada, o HTTPS/certificado, a porta 17831 e a visibilidade da impressora para o serviço. Não coloque tokens em URLs, capturas ou logs públicos.

## O que precisa ser atualizado em cada situação

| Mudança | Ação |
|---|---|
| Tela, mensagens ou adaptador Common | Compilar e publicar o aplicativo consumidor |
| Bibliotecas públicas Server/Client/Contracts | Gerar/publicar nova versão NuGet e atualizar o consumidor |
| Executável do agente | Gerar/publicar instalador, atualizar Bootstrap/hash e distribuir pacote correspondente; usuário executa atualização |
| Documentação do GitHub | Atualizar arquivos e fazer commit/push; não modifica pacotes já publicados |

Os ajustes atuais de conexão, versão e encapsulamento estão no consumidor Common e na página. Eles utilizam o agente Windows 0.3.0 e o NuGet 0.3.0-preview.1 existentes; não exigem outro instalador nem substituir um pacote já publicado.
