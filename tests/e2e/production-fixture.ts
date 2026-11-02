/** Align legacy planning fixtures with the physical-production read contract. */
export function productionFromPlan(plan: any) {
  const byMachine = new Map<string, any>();
  for (const row of plan.machineDemand ?? []) {
    if (!row.machineId) continue;
    const machine = byMachine.get(row.machineId) ?? { machineId: row.machineId, machineNumber: row.machineNumber, machineName: row.machineName,
      assignedQuantity: 0, producedQuantity: 0, pendingQuantity: 0, allocatedQuantity: 0, availableQuantity: 0 };
    if (row.unitCode === 'kg') { machine.assignedQuantity += row.quantity; machine.pendingQuantity += row.quantity; }
    byMachine.set(row.machineId, machine);
  }
  return { date: plan.date, routeCode: plan.routeCode, summaryScope: 'all-routes', machines: [...byMachine.values()], batches: [], allocations: [],
    orders: (plan.orders ?? []).filter((order: any) => order.lines.some((line: any) => line.requiresMachine !== false)).map((order: any) => ({ ...order,
      orderVersion: order.orderVersion ?? 1,
      lines: order.lines.filter((line: any) => line.requiresMachine !== false).map((line: any) => ({ ...line, allocatedQuantity: 0, pendingAllocationQuantity: line.quantity })) })) };
}
