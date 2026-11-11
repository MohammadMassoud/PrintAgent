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
dotnet add package SinergiaAI.PrintAgent --version 2.1.0
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

## Uma API para texto, bytes e listas na versão 2.1

```csharp
await PrintAgent.SendAsync(printerId, texto);
await PrintAgent.SendAsync(printerId, bytes);
await PrintAgent.SendAsync(printerId, listaDeTextos);

var documentos = new[]
{
    new PrintDocument(printerId, texto),
    new PrintDocument(printerId, bytes, chaveSalva)
};
await PrintAgent.SendAsync(documentos);
```

A chave é opcional: sem uma chave informada, cada chamada simples ou com lista de textos/bytes cria novos trabalhos. Um documento preparado gera sua chave uma vez e a mantém quando reutilizado. Se precisar recuperar o envio após uma falha, persista o destino, os bytes e a chave antes de enviar. Nunca repita automaticamente uma chamada sem chave estável depois de uma resposta incerta.

Textos usam a codificação configurada, UTF-8 estrito por padrão. Bytes seguem sem conversão. Para codificação por documento, use `CreateTextDocument(printerId, texto, encoding: Encoding.ASCII)` e importe `System.Text`. O aplicativo e a impressora precisam concordar sobre a linguagem e a codificação. Não há conversão de PDF, HTML ou imagens.

O SDK 2.1.0 funciona com o agente Windows 2.0.0. Os métodos antigos continuam compatíveis. Veja o [guia da versão 2.1](docs/migration-2.1.md). A documentação XML da API continua em inglês.
