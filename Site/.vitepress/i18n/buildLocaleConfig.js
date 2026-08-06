import { navSchema } from './navSchema.js'

// Pure function: walks navSchema, pulling each node's label from dict[node.id], and produces the
// VitePress nav/sidebar shape for one locale. No literal text lives in this file or in config.mjs —
// every string comes from the dictionary passed in.
export function buildLocaleConfig(prefix, dict) {
  const nav = navSchema.map((top) => ({
    text: dict[top.id],
    link: prefix + top.slug,
  }))

  const sidebar = {}
  for (const top of navSchema) {
    sidebar[prefix + top.slug] = buildItems(top.children, dict, prefix)
  }

  return { nav, sidebar }
}

function buildItems(nodes, dict, prefix) {
  return nodes.map((node) => {
    const item = { text: dict[node.id] }
    if (node.slug) item.link = prefix + node.slug
    if (node.children) {
      item.items = buildItems(node.children, dict, prefix)
      item.collapsed = false
    }
    return item
  })
}
