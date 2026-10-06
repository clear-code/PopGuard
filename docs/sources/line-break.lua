function Header(elem)
  if elem.level >= 4 then
    -- 見出しの後に改行を挿入
    local line_break = pandoc.LineBreak()
    return {elem, line_break}
  end
  return elem
end
