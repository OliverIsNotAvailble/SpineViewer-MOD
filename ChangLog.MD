## SpineViewer - Local Custom Build Changelog

### Branch VLote — 0.1 (2026-10-31)

- Criada branch **VLote**, independente do \`main\`. Mantido o SpineViewer existente e o preview central, sem substituir o renderizador SFML nem as opções de exportação tradicionais.
- Nova aba **VLote** na lateral esquerda: selecionar pasta raiz pelo Explorer ou digitar caminho; scanner recursivo localiza \`.skel\`, \`.skel.bytes\` e \`.json\` com \`.atlas\` correspondente. Ignora permanentemente \`_OK\`, \`_Null\` e \`_VLote_Exports\` em qualquer profundidade, além de junctions/symlinks. Somente modelos em subpastas da raiz são processados.
- Fila **um modelo por vez** com preview real na área central, configurações de exportação na lateral (largura, altura, FPS, formato MP4/WebP/APNG/GIF/WebM/MKV/MOV, CRF, qualidade, velocidade, transparência quando suportada, looping, auto enquadramento e exportação de todas as animações).
- **EXPORTAR → OK** usa o motor FFmpeg e a câmera atual do SpineViewer, exporta cada animação individualmente para \`_VLote_Exports/<modelo>/\`, verifica que cada arquivo existe e tem bytes e só então classifica o modelo. Não sobrescreve exports anteriores (sufixo incremental).
- **NULL → _Null** marca o modelo como descartado sem exportar. Quando houver vários \`.skel\` na mesma subpasta, a pasta original **só é movimentada quando todos estiverem classificados**. Homogênea: move a pasta inteira para \`_OK\` ou \`_Null\`; mista: prepara duas cópias, remove de cada uma o \`.skel\`/atlas/exports de outra categoria e mantém texturas e recursos compartilhados em ambas; só então remove os arquivos originais.
- Pastas com o mesmo nome nunca são sobrescritas; usa \`(1)\`, \`(2)\` etc. Caminhos simbólicos são rejeitados por segurança nas cópias; em caso de falha no FFmpeg/movimentação, a origem é preservada.
- Progresso \`concluídos/total/restantes\` aparece **no lugar do console inferior** quando a aba VLote está ativa. O console volta ao selecionar as demais abas. Progresso de decisões pendentes de mover é salvo em \`.vlote-progress.json\` na raiz, permitindo retomar após interrupções.
- Proteções: desativa classificações enquanto exporta; Cancelar interrompe quando o motor permite; carrega apenas um modelo por vez, descarrega recursos antes de mover, impede arquivos de saída vazios, impede MP4 com transparência. Se não conseguir carregar, mantém botão NULL disponível.
- Alterados: \`MainWindow.xaml\`, \`MainWindow.xaml.cs\`, \`FFmpegVideoExporterViewModel.cs\`, \`SpineViewer.csproj\`, \`ChangLog.MD\`. Novos: \`VLotePanel.xaml(.cs)\`, \`Services/VLoteQueue.cs\`, \`Services/VLoteExportOptions.cs\`, workflow de teste \`vlote-build.yml\`. \`README\` intacto.
- Limitações da v0.1: movimentação por **pasta de primeiro nível** (não arquivos soltos diretamente na raiz); configurador de lote exclusivo na aba VLote, sem atalho adicional na aba Canvas nesta versão. O visual real e a compatibilidade dos arquivos variam conforme a versão Spine e requerem teste com arquivos reais.
- Verificação: CI inicial falhou somente porque o restore não incluía o RID `win-x64` (NETSDK1047). Workflow corrigido com `dotnet restore -r win-x64`. Os imports explícitos do projeto WPF foram corrigidos. **Windows .NET 8 CI PASSOU**, incluindo publicação do executável: https://github.com/OliverIsNotAvailble/SpineViewer-MOD/actions/runs/41214313121 — artifact `SpineViewer-VLote-0.1-Windows` (ID `13046574254`, aproximadamente 9,9 MB). O preview em arquivos reais e os movimentos de pasta devem ser testados pelo usuário antes de aplicar a um acervo sem backup.



### V3

* Adicionado suporte para exportar vídeos com imagem de fundo embutida.
* A imagem de BG selecionada no preview agora também é renderizada no export FFmpeg.
* O export respeita a viewport atual, posição, zoom e resolução custom configurada no programa.
* Permite exportar MP4/GIF/WebP/APNG/MOV já com fundo pronto, sem precisar juntar BG manualmente em editor de vídeo.
* Mantém compatibilidade com export transparente quando nenhuma imagem de fundo está selecionada.

Arquivos alterados:

* `Spine/Exporters/BaseExporter.cs`
* `SpineViewer/ViewModels/Exporters/FFmpegVideoExporterViewModel.cs`

### V2

* Adicionada opção para exportar todas as animações de uma vez.
* Adicionado checkbox `Export all animations` na janela de export FFmpeg.
* Export All usa a viewport atual do preview, mantendo alinhamento manual com BG/posição/zoom.
* Corrigido problema de attachments/nodes herdados entre animações usando reset de setup pose antes de aplicar cada animação.
* Alterada nomenclatura dos arquivos exportados para usar apenas o nome da animação.

  * Exemplo: `idle.mp4`, `walk.mp4`, `attack.mp4`
* Removidos timestamp, hash, FPS e nome do modelo do nome final do arquivo.

Arquivos alterados:

* `SpineViewer/ViewModels/Exporters/VideoExporterViewModel.cs`
* `SpineViewer/Views/ExporterDialogs/FFmpegVideoExporterDialog.xaml`

### Observações de uso

* Para manter o enquadramento correto, usar `AutoResolution` desligado.
* Recomendo definir a resolução custom baseada no tamanho do BG.
* Se houver animações com nomes repetidos, arquivos podem ser sobrescritos.
* O BG embutido usa a imagem atualmente selecionada no preview.
