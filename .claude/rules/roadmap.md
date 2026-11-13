# Regra: manter o ROADMAP.md em dia

O `ROADMAP.md` é o quadro do que está feito e do que falta. Ele só serve se refletir a realidade.

- **Fez algo que está no roadmap?** No mesmo PR, troque o `- [ ]` do item por `- [x]` (se fez só uma parte, deixe `[ ]` e diga no texto o que
  já foi, como o item da #15 faz). Item que você concluiu e que não estava no roadmap: acrescente já marcado, na versão do ciclo.
- **Subiu a versão?** (abriu o ciclo `beta/X.Y.Z`, saiu beta ou saiu estável): atualize no `ROADMAP.md` a data de "Status em", a linha da
  versão na tabela "Resumo das versões" (em andamento, `beta.N` publicada, fechada) e o bloco "Onde estamos" (release e issues abertas).
- Issue fechada sai de "Issues abertas"; issue nova que entra no ciclo entra lá e na seção da versão.
- Não marque `[x]` o que depende de hardware, tablet ou rede que você não pôde provar: deixe `[ ]` e escreva o que falta provar.
- A marcação vai junto do commit que faz o trabalho (ou num commit `docs: roadmap ...` do mesmo PR), nunca "depois, num PR solto".
- Se o roadmap e a realidade discordam e você não sabe qual está certo, pergunte antes de mexer.
