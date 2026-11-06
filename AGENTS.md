# SinergiaPrint

- Projeto independente de SinergiaAI; não adicionar regras de restaurante, pedidos ou produtos ao agente.
- O agente envia bytes RAW pelo Windows/TCP sem validar linguagem ou converter documentos.
- SDK e chaves de backend pertencem ao servidor; nunca expor a chave em WebAssembly.
- Nunca reenviar automaticamente um trabalho que possa já ter alcançado a impressora.
- Preservar diário local e idempotência nas mudanças.
- Após mudanças no fluxo, executar `dotnet build SinergiaPrint.slnx` e `python tests/integration.py` no Windows.
- Não instalar serviço permanente nem enviar para uma impressora física durante os testes automatizados.
