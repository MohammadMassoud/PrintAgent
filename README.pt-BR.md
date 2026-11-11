# PrintAgent by SinergiaAI — ponte de impressão RAW para Blazor e .NET

O **SinergiaAI.PrintAgent** é uma ponte de impressão RAW entre aplicativos Blazor Server / .NET 10 e impressoras Windows ou TCP. Atende impressoras de etiquetas, térmicas e de papel comum que aceitem os comandos enviados pelo aplicativo. Um agente no computador Windows recebe os bytes pelo navegador e os encaminha para uma impressora Windows ou um destino TCP configurado.

O projeto é independente, gratuito e de código aberto com licença MIT. Pode ser utilizado em diferentes aplicativos que precisem de impressão local.

[Site e download](https://www.sinergiaai.com/printagent/pt-br.html) · [Pacote NuGet](https://www.nuget.org/packages/SinergiaAI.PrintAgent) · [English documentation](README.md)

## Como imprimir etiquetas no Blazor Server

1. Instale o agente Windows no computador conectado à impressora.
2. Adicione o pacote NuGet ao aplicativo Blazor Server ou Interactive Server com .NET 10.
3. Registre a integração, conecte o componente e autorize a origem do site na janela do Windows.
4. Selecione uma impressora compatível e envie os bytes gerados pelo aplicativo com uma chave estável.
5. Acompanhe o resultado pela integração.

```shell
dotnet add package SinergiaAI.PrintAgent --version 2.0.0
```

No `Program.cs`:

```csharp
using SinergiaAI.PrintAgent;

builder.Services.AddPrintAgent();

// Após app.Build() e os middlewares normais do aplicativo:
app.MapPrintAgent();
```

O navegador se conecta ao agente por WebSocket local. A fila persistente fica no computador Windows. O servidor do aplicativo não precisa acessar diretamente a impressora USB da estação.

Veja os guias com exemplos e cuidados de integração:

- [Como imprimir etiquetas ZPL no Blazor Server](https://www.sinergiaai.com/printagent/imprimir-etiquetas-blazor.html).
- [Como imprimir recibos ESC/POS com Blazor e C#](https://www.sinergiaai.com/printagent/impressao-escpos-blazor.html).
- [Integração completa e referência da API](docs/integration.md).
- [Diagnóstico de erros](docs/troubleshooting.md).

## Quais impressoras funcionam?

Use impressoras instaladas no Windows e acessíveis à conta do serviço, ou destinos TCP configurados. O modelo precisa entender a linguagem dos bytes enviados: por exemplo, ZPL para uma Zebra compatível ou ESC/POS para uma impressora térmica compatível. A compatibilidade deve ser conferida na documentação do equipamento.

O PrintAgent transporta os bytes RAW sem validar a linguagem e sem converter HTML, PDF, documentos Office ou imagens. O layout e os comandos são responsabilidade do aplicativo. Esta integração atende Blazor Server / Interactive Server; não há SDK independente para Blazor WebAssembly.

## Como evitar impressões duplicadas?

Salve a chave, o destino e os bytes antes do envio. A mesma chave com o mesmo conteúdo retorna o trabalho original. Não crie outra chave automaticamente após timeout ou perda de conexão. O estado `Sent` confirma o envio ao spooler ou destino TCP, sem garantir a saída física do papel. Resultados `Unknown` exigem conferência antes de autorizar outra cópia.

## Como testar sem impressora?

```shell
git clone https://github.com/MohammadMassoud/PrintAgent.git
cd PrintAgent
dotnet run --project samples/PrintAgent.Demo -- --simulate
```

Abra `http://localhost:5199`. A demonstração usa um agente temporário e um receptor TCP local, exclui impressoras físicas e não instala serviço permanente. Requer Windows x64 e SDK .NET 10. A simulação verifica o fluxo de bytes, não a aparência final no papel.

## Atualização da linha 0.x

A versão 1.0.0 usa protocolo 2 e exige atualizar o pacote e o agente. Os diários e dados antigos devem ser preservados para conferência, sem reenvio automático. Consulte o [guia de migração](docs/migration-1.0.md).

## Envio de texto e bytes na versão 2.0

```csharp
// Texto: UTF-8 por padrão, configurável na integração ou por envio.
await PrintAgent.SendAsync(printerId, commands, textRequestKey);

// Binário: os bytes seguem sem conversão.
await PrintAgent.SendByteAsync(printerId, rawBytes, byteRequestKey);

// Lote com texto preparado e bytes. Persista os documentos antes de enviar.
var text = PrintAgent.CreateTextDocument(printerId, commands, textKey);
var binary = new PrintDocument(printerId, rawBytes, binaryKey);
await PrintAgent.SendBatchAsync(new[] { text, binary });
```

Configure a codificação padrão com `AddPrintAgent(o => o.TextEncoding = Encoding.ASCII)` ou informe uma por envio: `SendAsync(printerId, commands, requestKey, Encoding.UTF8)`. Importe `System.Text`. A impressora precisa compreender a codificação escolhida. Caracteres incompatíveis são rejeitados; não há conversão de linguagem, adição de BOM ou interpretação de textos como `0x1B` como escapes.

A chave identifica uma solicitação, não é senha nem `CancellationToken`. Guarde o destino, os bytes exatos e a chave antes do primeiro envio. Cancelar a espera não cancela um trabalho já aceito.

Impressoras de papel comum também podem ser usadas com linguagens compatíveis, como PCL ou PostScript nos modelos que as suportam. O agente não renderiza PDF, HTML ou imagens. O serviço padrão também inclui impressoras compartilhadas dos usuários com sessão ativa; ele não varre a rede em busca de equipamentos.

Ao atualizar de 1.x, troque chamadas com bytes de `SendAsync` por `SendByteAsync`. Consulte a [migração para 2.0](docs/migration-2.0.md). A documentação XML da API é publicada em inglês para IntelliSense e navegação por metadados no F12.
