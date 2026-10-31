## SpineViewer - Local Custom Build Changelog

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
