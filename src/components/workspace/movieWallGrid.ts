export function movieWallColumnCount(availableWidth: number, cardWidth: number, gap: number) {
  if (!Number.isFinite(availableWidth) || availableWidth <= 0) return 1
  return Math.max(1, Math.floor((availableWidth + gap) / (cardWidth + gap)))
}
